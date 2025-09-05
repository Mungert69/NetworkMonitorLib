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
    /// Wakes a sleeping Hugging Face Space by auto-clicking the "Restart this Space" button.
    /// </summary>
    public class HugSpaceWakeCmdProcessor : CmdProcessor
    {
        private readonly int _microTimeout;  // selector waits
        private readonly int _macroTimeout;  // overall budget
        private readonly ILaunchHelper? _launchHelper;

        public HugSpaceWakeCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
            ILaunchHelper? launchHelper = null,
            int microTimeoutMs = 10000,
            int macroTimeoutMs = 60000)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _launchHelper = launchHelper;
            _microTimeout = microTimeoutMs;
            _macroTimeout = macroTimeoutMs;
        }

        public override async Task<ResultObj> RunCommand(string url, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj();

            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    var m = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent.";
                    _logger.LogWarning("Warning: {msg}", m);
                    result.Message = await SendMessage(m, processorScanDataObj);
                    result.Success = false;
                    return result;
                }

                if (_launchHelper == null)
                {
                    const string m = "PuppeteerSharp browser is not available on this agent. Check the installation.";
                    _logger.LogWarning("Error: {msg}", m);
                    result.Message = await SendMessage(m, processorScanDataObj);
                    result.Success = false;
                    return result;
                }

                cancellationToken.ThrowIfCancellationRequested();

                bool headless = _launchHelper.CheckDisplay(_logger, _netConfig.ForceHeadless);
                var launchOptions = await _launchHelper.GetLauncher(_netConfig.CommandPath, _logger, headless);

                using var browser = await Puppeteer.LaunchAsync(launchOptions);
                using var page = await browser.NewPageAsync();
                page.DefaultTimeout = _microTimeout;

                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                opCts.CancelAfter(_macroTimeout);

                // 1) Go to the supplied URL
                await page.GoToAsync(url, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

                // 2) If this is the iframe subdomain (.hf.space), try to jump to the canonical huggingface page
                string currentUrl = page.Url;
                if (!IsHfWrapper(currentUrl))
                {
                    var canonical = await page.EvaluateExpressionAsync<string>(@"
                        (function(){
                          const link = document.querySelector('link[rel=""canonical""]');
                          return link ? link.getAttribute('href') : '';
                        })()
                    ") ?? string.Empty;

                    if (IsHfWrapper(canonical))
                    {
                        _logger.LogInformation("Discovered canonical HF Space page: {canonical}", canonical);
                        await page.GoToAsync(canonical, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });
                        currentUrl = page.Url;
                    }
                }

                // 3) Already running?
                if (await IsSpaceRunning(page))
                {
                    result.Success = true;
                    result.Message = await SendMessage("Space appears to be running (no restart needed).", processorScanDataObj);
                    return result;
                }

                // 4) Find & click restart
                var clicked = await TryClickRestart(page, _logger, opCts.Token);
                if (!clicked)
                {
                    if (await IsSpaceRunning(page))
                    {
                        result.Success = true;
                        result.Message = await SendMessage("Space is running.", processorScanDataObj);
                        return result;
                    }

                    var failMsg = "Could not find or click the 'Restart this Space' button. Ensure the Space is public and restartable without auth.";
                    _logger.LogWarning(failMsg);
                    result.Success = false;
                    result.Message = await SendMessage(failMsg, processorScanDataObj);
                    return result;
                }

                _logger.LogInformation("Clicked restart; waiting for the Space to wake...");

                // 5) Wait for running indicators
                var woke = await WaitForSpaceToRun(page, _logger, opCts.Token);
                if (woke)
                {
                    result.Success = true;
                    result.Message = await SendMessage("Space restarted successfully.", processorScanDataObj);
                }
                else
                {
                    result.Success = false;
                    result.Message = await SendMessage("Clicked restart, but the Space did not become ready within the timeout.", processorScanDataObj);
                }

                return result;
            }
            catch (OperationCanceledException oce)
            {
                _logger.LogWarning("HugSpaceWake canceled/timeout: {msg}", oce.Message);
                return new ResultObj { Success = false, Message = "Operation canceled or timed out.\n" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while waking HF Space: {err}", ex.Message);
                return new ResultObj { Success = false, Message = $"Error waking HF Space: {ex.Message}\n" };
            }
        }

        public override string GetCommandHelp() => @"
Wakes a Hugging Face Space if it's sleeping by clicking the 'Restart this Space' button.

Usage:
  arguments: https://huggingface.co/spaces/{owner}/{space}
  (You can also pass https://{owner}-{space}.hf.space; we'll jump to the canonical page if discoverable.)

Behavior:
  • Detects if the Space is already running (iframe present / 'Sleeping' badge absent).
  • If sleeping, auto-clicks the restart button, then waits for the Space to report running.

Notes:
  • Works for public Spaces that allow anonymous restarts.
  • Timeouts tunable via constructor (microTimeoutMs, macroTimeoutMs).";

        // ——— helpers ———

        private static bool IsHfWrapper(string? url)
            => !string.IsNullOrEmpty(url) && url.Contains("huggingface.co/spaces/", StringComparison.OrdinalIgnoreCase);

        private static async Task<bool> IsSpaceRunning(IPage page)
        {
            try
            {
                // iframe to *.hf.space?
                var hasIframe = await page.EvaluateExpressionAsync<bool>(@"!!document.querySelector('iframe[src*="".hf.space""]')");
                if (hasIframe) return true;

                // 'Sleeping' badge?
                var hasSleepingBadge = await page.EvaluateExpressionAsync<bool>(@"
                    !![...document.querySelectorAll('div,span,button')].some(el => /(^|\s)Sleeping(\s|$)/i.test(el.textContent || ''))
                ");
                if (hasSleepingBadge) return false;

                // restart form present?
                var hasRestartForm = await page.EvaluateExpressionAsync<bool>(@"!!document.querySelector('form[action$=""/start""] button[type=""submit""]')");
                return !hasRestartForm;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> TryClickRestart(IPage page, ILogger logger, CancellationToken ct)
        {
            try
            {
                // Try stable selector first
                IElementHandle? handle = await page.QuerySelectorAsync(@"form[action$=""/start""] button[type=""submit""]");
                if (handle == null)
                {
                    // Fallback: find by text
                    handle = await FindButtonByText(page, "Restart this Space", ct);
                }

                if (handle == null)
                {
                    logger.LogDebug("Restart button not found.");
                    return false;
                }

                await handle.ClickAsync();
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to click restart button.");
                return false;
            }
        }

        private static async Task<IElementHandle?> FindButtonByText(IPage page, string text, CancellationToken ct)
        {
            try
            {
                // Return the actual ElementHandle from the page execution context
                var js = @"
                    (function(txt){
                      const norm = s => (s || '').replace(/\s+/g,' ').trim().toLowerCase();
                      const target = norm(txt);
                      const nodes = Array.from(document.querySelectorAll('button, [role=""button""]'));
                      return nodes.find(b => norm(b.textContent) === target) || null;
                    })
                ";
                IJSHandle handle = await page.EvaluateFunctionHandleAsync(js, text);
                return handle as IElementHandle; // cast instead of .AsElement()
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> WaitForSpaceToRun(IPage page, ILogger logger, CancellationToken ct)
        {
            try
            {
                var pollDelay = TimeSpan.FromMilliseconds(500);
                var deadline  = DateTime.UtcNow + TimeSpan.FromSeconds(50);

                while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
                {
                    if (await IsSpaceRunning(page)) return true;
                    try { await page.WaitForNetworkIdleAsync(new() { IdleTime = 500, Timeout = 2000 }); } catch { /* ignore */ }
                    await Task.Delay(pollDelay, ct);
                }

                return await IsSpaceRunning(page);
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("WaitForSpaceToRun canceled.");
                return false;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "WaitForSpaceToRun exception.");
                return false;
            }
        }
    }
}
