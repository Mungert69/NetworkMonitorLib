using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Connection
{
    /// <summary>
    /// Generates real client activity against a Hugging Face Space so it does not go idle.
    /// Accepts either wrapper URL or app URL.
    /// </summary>
    public class HugSpaceKeepAliveCmdProcessor : CmdProcessor, ICmdProcessorFactory
    {
        private readonly int _microTimeoutMs;
        private readonly int _macroTimeoutMs;
        private readonly int _lingerMs;
        private readonly ILaunchHelper? _launchHelper;

        private const int DefaultMicroTimeoutMs = 10_000;
        private const int DefaultMacroTimeoutMs = 45_000;
        private const int DefaultLingerMs      = 8_000;

        public HugSpaceKeepAliveCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
            ILaunchHelper? launchHelper = null
        ) : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _launchHelper   = launchHelper;
            _microTimeoutMs = DefaultMicroTimeoutMs;
            _macroTimeoutMs = DefaultMacroTimeoutMs;
            _lingerMs       = DefaultLingerMs;
        }

        public static string TypeKey => "HugSpaceKeepAlive";
        public static ICmdProcessor Create(ILogger l, ILocalCmdProcessorStates s, IRabbitRepo r, NetConnectConfig c, ILaunchHelper? h = null)
            => new HugSpaceKeepAliveCmdProcessor(l, s, r, c, h);

        public override async Task<ResultObj> RunCommand(
            string url, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
        {
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    var m = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent.";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }

                if (_launchHelper == null)
                {
                    const string m = "PuppeteerSharp browser is not available on this agent.";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }

                cancellationToken.ThrowIfCancellationRequested();

                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                opCts.CancelAfter(_macroTimeoutMs);

                // Open prepared browser session (UA, headers, stealth, viewport)
                var session = await WebAutomationHelper.OpenSessionAsync(
                    _launchHelper, _netConfig, _logger, opCts.Token, _microTimeoutMs);

                await using var _ = session;

                // Resolve wrapper -> app origin if needed
                var targetAppUrl = await WebAutomationHelper.ResolveHuggingFaceAppOriginAsync(
                    session.Page, url, _logger, opCts.Token);

                // Navigate with cache-buster
                _logger.LogInformation("KeepAlive: navigating to {url}", targetAppUrl);
                await WebAutomationHelper.GoToWithCacheBusterAsync(session.Page, targetAppUrl);

                // If Gradio, wait for queue traffic (or just delay ~8s)
                await WebAutomationHelper.WaitForGradioQueueOrDelayAsync(
                    session.Page, TimeSpan.FromSeconds(8), opCts.Token);

                // Extra fetch to ensure an additional server hit (helps static sites)
                await WebAutomationHelper.FireNoStoreFetchAsync(session.Page, "/");

                // Let it idle a bit, then linger
                await WebAutomationHelper.WaitForNetworkIdleSafeAsync(session.Page, idleMs: 800, timeoutMs: 5000);

                if (_lingerMs > 0)
                {
                    _logger.LogInformation("KeepAlive: lingering for {ms}ms", _lingerMs);
                    await Task.Delay(_lingerMs, opCts.Token);
                }

                return new ResultObj
                {
                    Success = true,
                    Message = await SendMessage($"Keep-alive ping completed for {targetAppUrl}", processorScanDataObj)
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
  arguments: https://huggingface.co/spaces/{owner}/{space}
  or       : https://{owner}-{space}.hf.space

Behavior:
  • If given the wrapper URL, resolves the iframe app origin and navigates there.
  • Loads with cache-busting & no-store headers, triggers JS & (if Gradio) queue calls.
  • Sends one extra fetch and lingers briefly so sockets stay open.";
    }
}
