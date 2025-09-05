using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Connection;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects.ServiceMessage;    

namespace NetworkMonitor.Connection
{
    /// <summary>
    /// Generates real client activity against a Hugging Face Space so it does not go idle.
    /// Accepts either wrapper URL or app URL.
    /// </summary>
    public class HugSpaceKeepAliveCmdProcessor : CmdProcessor
    {
        private readonly int _microTimeoutMs;
        private readonly int _macroTimeoutMs;
        private readonly int _lingerMs;
        private readonly ILaunchHelper? _launchHelper;

        public HugSpaceKeepAliveCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
            ILaunchHelper? launchHelper = null,
            int microTimeoutMs = 10000,
            int macroTimeoutMs = 45000,
            int lingerMs = 8000)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _launchHelper = launchHelper;
            _microTimeoutMs = microTimeoutMs;
            _macroTimeoutMs = macroTimeoutMs;
            _lingerMs = lingerMs;
        }

        public override async Task<ResultObj> RunCommand(string url, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
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

                bool headless = _launchHelper.CheckDisplay(_logger, _netConfig.ForceHeadless);
                var launchOptions = await _launchHelper.GetLauncher(_netConfig.CommandPath, _logger, headless);

                using var browser = await Puppeteer.LaunchAsync(launchOptions);
                using var page = await browser.NewPageAsync();
                page.DefaultTimeout = _microTimeoutMs;
                await page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 800 });

                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                opCts.CancelAfter(_macroTimeoutMs);

                // 1) Normalize to app origin if wrapper URL given
                var targetAppUrl = await ResolveAppOrigin(url, page, _logger, opCts.Token);

                // 2) Navigate to app origin (this counts as real activity)
                _logger.LogInformation("KeepAlive: navigating to {url}", targetAppUrl);
                await page.GoToAsync(targetAppUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

                // 3) Look for Gradio queue traffic to ensure JS has executed
                var queueSeenTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void MaybeResolveQueue(PuppeteerSharp.IRequest req)
                {
                    try
                    {
                        var u = req.Url ?? "";
                        if (u.Contains("/queue/join", StringComparison.OrdinalIgnoreCase) ||
                            u.Contains("/queue/data", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!queueSeenTcs.Task.IsCompleted) queueSeenTcs.TrySetResult(true);
                        }
                    }
                    catch { /* ignore */ }
                }

                page.Request += (_, e) => MaybeResolveQueue(e.Request);

                var queueTask = queueSeenTcs.Task;
                var delayTask = Task.Delay(8000, opCts.Token);
                await Task.WhenAny(queueTask, delayTask);

                // 4) Let it idle then linger
                try { await page.WaitForNetworkIdleAsync(new() { IdleTime = 800, Timeout = 5000 }); } catch { /* ignore */ }
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
  • Runs JS long enough for the front-end to open its queue/websocket endpoints.
  • Lingers briefly so sockets stay open.";

        private static bool IsWrapperUrl(string u) =>
            !string.IsNullOrWhiteSpace(u) && u.Contains("huggingface.co/spaces/", StringComparison.OrdinalIgnoreCase);

        private static async Task<string> ResolveAppOrigin(string inputUrl, IPage page, ILogger logger, CancellationToken ct)
        {
            if (!IsWrapperUrl(inputUrl)) return inputUrl;

            await page.GoToAsync(inputUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

            // Primary: parse Svelte hydrater props / direct iframe src
            var appUrl = await page.EvaluateExpressionAsync<string>(@"
                (function () {
                  try {
                    const hydraters = [...document.querySelectorAll('.SVELTE_HYDRATER[data-target=""SpacePageInner""]')];
                    for (const el of hydraters) {
                      const props = el.getAttribute('data-props');
                      if (props) {
                        try {
                          const obj = JSON.parse(props);
                          if (obj && obj.space && obj.space.iframe && obj.space.iframe.src) return obj.space.iframe.src;
                          if (obj && obj.iframeSrc) return obj.iframeSrc;
                        } catch {}
                      }
                    }
                  } catch {}
                  const ifr = document.querySelector('iframe[src*="".hf.space""]');
                  if (ifr) return ifr.getAttribute('src');
                  return '';
                })()
            ") ?? "";

            if (!string.IsNullOrWhiteSpace(appUrl)) return appUrl;

            // Fallback: scan body for a visible hf.space URL
            var sub = await page.EvaluateExpressionAsync<string>(@"
                (function(){
                  const t = document.body ? document.body.innerHTML : '';
                  const m = t.match(/https?:\/\/([a-z0-9-]+)\.hf\.space/ig);
                  return m && m[0] ? m[0] : '';
                })()
            ") ?? "";

            if (!string.IsNullOrWhiteSpace(sub)) return sub;

            logger.LogInformation("Could not auto-resolve app origin; using the input URL as-is.");
            return inputUrl;
        }
    }
}
