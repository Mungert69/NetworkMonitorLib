using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;

namespace NetworkMonitor.Connection
{
    /// <summary>
    /// Generates real client activity against a Hugging Face Space so it does not go idle.
    /// Accepts either wrapper URL or direct app URL. Uses a shared BrowserHost to avoid
    /// spawning extra Chromium processes.
    /// </summary>
    public class HugSpaceKeepAliveCmdProcessor : CmdProcessor, ICmdProcessorFactory
    {
        // Defaults (can be overridden by CLI args)
        private const int DefaultMicroTimeoutMs = 10_000;
        private const int DefaultMacroTimeoutMs = 45_000;
        private const int DefaultLingerMs      = 8_000;

               private readonly IBrowserHost _browserHost;
        private readonly List<ArgSpec> _schema;

        public HugSpaceKeepAliveCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
             IBrowserHost? browserHost = null
        ) : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _browserHost  = browserHost ?? throw new ArgumentNullException(nameof(browserHost));

            // Fully specify schema so Parse(...) can validate + fill defaults
            _schema = new()
            {
                new()
                {
                    Key = "url",
                    Required = true,
                    IsFlag = false,
                    TypeHint = "url",
                    Help = "Hugging Face Space wrapper URL or app origin (https://huggingface.co/spaces/{owner}/{space} or https://{owner}-{space}.hf.space)"
                },
                new()
                {
                    Key = "micro_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultMicroTimeoutMs.ToString(),
                    Help = "Short per-wait timeout in ms (network idle, small waits)."
                },
                new()
                {
                    Key = "macro_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultMacroTimeoutMs.ToString(),
                    Help = "Overall operation timeout in ms."
                },
                new()
                {
                    Key = "linger_ms",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultLingerMs.ToString(),
                    Help = "How long to linger (ms) after activity to keep sockets warm."
                }
            };
        }

        public static string TypeKey => "HugSpaceKeepAlive";

        // Preferred factory (allows passing BrowserHost)
        public static ICmdProcessor Create(
            ILogger l,
            ILocalCmdProcessorStates s,
            IRabbitRepo r,
            NetConnectConfig c,
            IBrowserHost? bh = null)
            => new HugSpaceKeepAliveCmdProcessor(l, s, r, c, bh);

        // Required by ICmdProcessorFactory interface
        public static ICmdProcessor Create(
            ILogger logger,
            ILocalCmdProcessorStates states,
            IRabbitRepo repo,
            NetConnectConfig cfg,
            ILaunchHelper? launchHelper = null)
            => new HugSpaceKeepAliveCmdProcessor(logger, states, repo, cfg, null);

       
        public override async Task<ResultObj> RunCommand(
            string arguments,
            CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            try
            {
                // Availability checks first
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    var m = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent.";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }

                // Parse + validate args with defaults filled
                var parseResult = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
                if (!parseResult.Success)
                {
                    var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parseResult, _schema);
                    _logger.LogWarning("Arguments not valid {args}. {msg}", arguments, parseResult.Message);
                    return new ResultObj { Success = false, Message = await SendMessage(err, processorScanDataObj) };
                }

                var url          = parseResult.GetString("url"); // validated "url" type
                var microTimeout = parseResult.GetInt("micro_timeout", DefaultMicroTimeoutMs);
                var macroTimeout = parseResult.GetInt("macro_timeout", DefaultMacroTimeoutMs);
                var lingerMs     = parseResult.GetInt("linger_ms", DefaultLingerMs);

                cancellationToken.ThrowIfCancellationRequested();

                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                opCts.CancelAfter(macroTimeout);

                // Use the shared BrowserHost; do all work on a single gated page
                var (ok, msg) = await _browserHost.RunWithPage(async page =>
                {
                    page.DefaultTimeout = microTimeout;

                    // Resolve wrapper -> app origin if needed
                    var targetAppUrl = await WebAutomationHelper.ResolveHuggingFaceAppOriginAsync(
                        page, url, _logger, opCts.Token);

                    // Navigate with cache-buster
                    _logger.LogInformation("KeepAlive: navigating to {url}", targetAppUrl);
                    await WebAutomationHelper.GoToWithCacheBusterAsync(page, targetAppUrl);

                    // If Gradio, wait for queue traffic (or delay ~8s)
                    await WebAutomationHelper.WaitForGradioQueueOrDelayAsync(
                        page, TimeSpan.FromSeconds(8), opCts.Token);

                    // Extra fetch to ensure an additional server hit (helps static sites)
                    await WebAutomationHelper.FireNoStoreFetchAsync(page, "/");

                    // Let it idle a bit
                    await WebAutomationHelper.WaitForNetworkIdleSafeAsync(
                        page, idleMs: 800, timeoutMs: Math.Max(2000, microTimeout / 5));

                    // Linger to keep sockets warm
                    if (lingerMs > 0)
                    {
                        _logger.LogInformation("KeepAlive: lingering for {ms}ms", lingerMs);
                        await Task.Delay(lingerMs, opCts.Token);
                    }

                    return (true, $"Keep-alive ping completed for {targetAppUrl}");
                }, opCts.Token);

                return new ResultObj
                {
                    Success = ok,
                    Message = await SendMessage(msg, processorScanDataObj)
                };
            }
            catch (OperationCanceledException)
            {
                return new ResultObj { Success = false, Message = "Keep-alive canceled or timed out.\n" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Keep-alive error: {err}", ex.Message);
                return new ResultObj { Success = false, Message = $"Keep-alive error: {ex.Message}\n" };
            }
        }

        public override string GetCommandHelp() => @"
Keeps a Hugging Face Space from going idle by generating real client activity against the app origin.

Usage:
  --url <url> [--micro_timeout <int>] [--macro_timeout <int>] [--linger_ms <int>]

Required:
 only --url  is required.
 
Examples:
  --url https://huggingface.co/spaces/owner/space
  --url https://owner-space.hf.space --linger_ms 5000

Behavior:
  • If given the wrapper URL, resolves the iframe app origin and navigates there.
  • Loads with cache-busting & no-store headers, triggers JS & (if Gradio) queue calls.
  • Sends one extra fetch and lingers briefly so sockets stay open.";
    }
}
