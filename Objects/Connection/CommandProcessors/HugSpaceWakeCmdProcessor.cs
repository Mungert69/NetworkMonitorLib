using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
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
    /// Wakes a sleeping Hugging Face Space by auto-clicking the "Restart this Space" button.
    /// Accepts wrapper URL or the app subdomain; will hop to the canonical wrapper if detected.
    /// Uses a shared BrowserHost to avoid spawning extra Chromium processes.
    /// </summary>
    public class HugSpaceWakeCmdProcessor : CmdProcessor, ICmdProcessorFactory
    {
        private enum SpaceStage
        {
            Unknown,
            Running,
            Sleeping
        }

        private readonly record struct SpaceProbeResult(SpaceStage Stage, string? AppUrl);

        private static readonly HttpClient ProbeClient = CreateProbeClient();
        private static readonly Regex IframeSrcRegex = new(
            @"<iframe[^>]+src=""(?<src>https://[^""]+\.hf\.space[^""]*)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly IBrowserHost? _browserHost;
        private readonly List<ArgSpec> _schema;

        // Defaults (override via CLI)
        private const int DefaultMicroTimeoutMs = 10_000;
        private const int DefaultMacroTimeoutMs = 60_000;

        public HugSpaceWakeCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
             IBrowserHost? browserHost = null
        ) : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _browserHost  = browserHost;

            _schema = new()
            {
                new()
                {
                    Key = "url",
                    Required = true,
                    IsFlag = false,
                    TypeHint = "url",
                    Help = "HF wrapper URL or app origin (https://huggingface.co/spaces/{owner}/{space} or https://{owner}-{space}.hf.space)"
                },
                new()
                {
                    Key = "micro_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultMicroTimeoutMs.ToString(),
                    Help = "Per-step timeout in ms (page ops, small waits)."
                },
                new()
                {
                    Key = "macro_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultMacroTimeoutMs.ToString(),
                    Help = "Overall operation timeout in ms."
                }
            };
        }

        public static string TypeKey => "HugSpaceWake";

        // Prefer this factory so providers can pass the BrowserHost
        public static ICmdProcessor Create(
            ILogger l,
            ILocalCmdProcessorStates s,
            IRabbitRepo r,
            NetConnectConfig c,
            IBrowserHost? bh = null)
            => new HugSpaceWakeCmdProcessor(l, s, r, c, bh);

           
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

                if (_browserHost == null)
                {
                    const string m = "Browser host is not available on this agent. Ensure the shared BrowserHost is registered and passed into the processor.";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }

                // Parse + validate args (with defaults)
                var parseResult = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
                if (!parseResult.Success)
                {
                    var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parseResult, _schema);
                    _logger.LogWarning("Arguments not valid {args}. {msg}", arguments, parseResult.Message);
                    return new ResultObj { Success = false, Message = await SendMessage(err, processorScanDataObj) };
                }

                var url          = parseResult.GetString("url"); // validated URL
                var microTimeout = parseResult.GetInt("micro_timeout", DefaultMicroTimeoutMs);
                var macroTimeout = parseResult.GetInt("macro_timeout", DefaultMacroTimeoutMs);
                var debugParts   = new List<string>();
                SpaceProbeResult? probeResult = null;

                // Lightweight probe using static HTML to avoid spinning up Chromium if not needed.
                if (IsHfWrapper(url))
                {
                    try
                    {
                        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        probeCts.CancelAfter(microTimeout);
                        var probe = await ProbeSpaceStateAsync(url, probeCts.Token);
                        probeResult = probe;

                        if (probe.Stage != SpaceStage.Unknown)
                            debugParts.Add($"probe_stage={probe.Stage}");
                        if (!string.IsNullOrWhiteSpace(probe.AppUrl))
                            debugParts.Add($"probe_iframe={probe.AppUrl}");

                        if (probe.Stage == SpaceStage.Running)
                        {
                            _logger.LogInformation("WakeCmd: quick probe shows the space is already running.");
                            var message = "Space appears to be running - no restart needed." + FormatContext(debugParts);
                            return new ResultObj
                            {
                                Success = true,
                                Message = await SendMessage(message, processorScanDataObj)
                            };
                        }

                        if (!string.IsNullOrWhiteSpace(probe.AppUrl))
                        {
                            _logger.LogDebug("WakeCmd: quick probe resolved iframe source {src}", probe.AppUrl);
                        }

                        if (probe.Stage == SpaceStage.Sleeping)
                        {
                            _logger.LogInformation("WakeCmd: quick probe detected a sleeping space; proceeding with restart automation.");
                        }
                        else
                        {
                            _logger.LogDebug("WakeCmd: quick probe inconclusive; falling back to automation.");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogDebug("WakeCmd: quick probe cancelled/timed out; continuing with automation.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "WakeCmd: quick probe failed; continuing with automation.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                opCts.CancelAfter(macroTimeout);

                // Use the shared BrowserHost; do all work on a single gated page
                var (ok, msg) = await _browserHost.RunWithPage(async page =>
                {
                    page.DefaultTimeout = microTimeout;
                    debugParts.Add($"initial_url={url}");

                    // 1) Navigate to the supplied URL
                    await page.GoToAsync(url, new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                    });

                    // 2) If we started from the app subdomain, try to hop to wrapper (canonical)
                    var currentUrl = page.Url;
                    debugParts.Add($"page_url={currentUrl}");
                    if (!IsHfWrapper(currentUrl))
                    {
                        var canonical = await page.EvaluateExpressionAsync<string>(@"
                            (function(){
                              const link = document.querySelector('link[rel=""canonical""]');
                              return link ? (link.getAttribute('href') || '') : '';
                            })()
                        ") ?? string.Empty;

                        if (IsHfWrapper(canonical))
                        {
                            _logger.LogInformation("Discovered canonical HF Space page: {canonical}", canonical);
                            debugParts.Add($"canonical={canonical}");
                            await page.GoToAsync(canonical, new NavigationOptions
                            {
                                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                            });
                            currentUrl = page.Url;
                            debugParts.Add($"page_url={currentUrl}");
                        }
                    }

                    // 3) Already running?
                    if (await IsSpaceRunning(page))
                    {
                        debugParts.Add($"final_url={page.Url}");
                        return (true, "Space appears to be running (no restart needed)." + FormatContext(debugParts));
                    }

                    // 4) Find & click restart
                    var clicked = await TryClickRestart(page, _logger, opCts.Token);
                    if (!clicked)
                    {
                        // Double-check in case it raced to running state
                        if (await IsSpaceRunning(page))
                        {
                            debugParts.Add($"final_url={page.Url}");
                            return (true, "Space is running." + FormatContext(debugParts));
                        }

                        var failMsg = "Could not find or click the 'Restart this Space' button. Ensure the Space is public and restartable without auth.";
                        _logger.LogWarning(failMsg);
                        debugParts.Add("restart_click=failed");
                        return (false, failMsg + FormatContext(debugParts));
                    }

                    _logger.LogInformation("Clicked restart; waiting for the Space to wake...");
                    debugParts.Add("restart_click=ok");

                    // 5) Wait for running indicators
                    var woke = await WaitForSpaceToRun(page, _logger, opCts.Token);
                    if (woke)
                    {
                        debugParts.Add($"final_url={page.Url}");
                        return (true, "Space restarted successfully." + FormatContext(debugParts));
                    }

                    debugParts.Add("wake_status=timeout");
                    return (false, "Clicked restart, but the Space did not become ready within the timeout." + FormatContext(debugParts));
                }, opCts.Token);

                return new ResultObj
                {
                    Success = ok,
                    Message = await SendMessage(msg, processorScanDataObj)
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("HugSpaceWake canceled or timed out.");
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
  --url <url> [--micro_timeout <int>] [--macro_timeout <int>]

Examples:
  --url https://huggingface.co/spaces/owner/space
  --url https://owner-space.hf.space

Required:
 only --url is required.
 
Behavior:
  • Detects if the Space is already running (iframe present / 'Sleeping' badge absent).
  • If sleeping, auto-clicks the restart button, then waits for the Space to report running.

Notes:
  • Works for public Spaces that allow anonymous restarts.";

        // ——— helpers ———

        private static bool IsHfWrapper(string? url)
            => !string.IsNullOrEmpty(url) &&
               url.Contains("huggingface.co/spaces/", StringComparison.OrdinalIgnoreCase);

        private static async Task<bool> IsSpaceRunning(IPage page)
        {
            try
            {
                // iframe to *.hf.space?
                var hasIframe = await page.EvaluateExpressionAsync<bool>(
                    @"!!document.querySelector('iframe[src*="".hf.space""]')");
                if (hasIframe) return true;

                // 'Sleeping' badge?
                var hasSleepingBadge = await page.EvaluateExpressionAsync<bool>(@"
                    !![...document.querySelectorAll('div,span,button')].some(el => /(^|\s)Sleeping(\s|$)/i.test(el.textContent || ''))
                ");
                if (hasSleepingBadge) return false;

                // restart form present?
                var hasRestartForm = await page.EvaluateExpressionAsync<bool>(
                    @"!!document.querySelector('form[action$=""/start""] button[type=""submit""]')");
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
                // Prefer a stable selector
                IElementHandle? handle = await page.QuerySelectorAsync(
                    @"form[action$=""/start""] button[type=""submit""]");

                if (handle == null)
                {
                    // Fallback: find by exact text
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
                var js = @"
                    (function(txt){
                      const norm = s => (s || '').replace(/\s+/g,' ').trim().toLowerCase();
                      const target = norm(txt);
                      const nodes = Array.from(document.querySelectorAll('button, [role=""button""]'));
                      return nodes.find(b => norm(b.textContent) === target) || null;
                    })
                ";
                var jsHandle = await page.EvaluateFunctionHandleAsync(js, text);
                if (jsHandle is IElementHandle element)
                {
                    return element;
                }

                await jsHandle.DisposeAsync();
                return null;
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
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(50);

                while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
                {
                    if (await IsSpaceRunning(page)) return true;
                    try
                    {
                        await page.WaitForNetworkIdleAsync(new() { IdleTime = 500, Timeout = 2000 });
                    }
                    catch { /* ignore */ }

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

        private static HttpClient CreateProbeClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(12)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            return client;
        }

        private static async Task<SpaceProbeResult> ProbeSpaceStateAsync(string wrapperUrl, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(wrapperUrl))
                return new SpaceProbeResult(SpaceStage.Unknown, null);

            using var request = new HttpRequestMessage(HttpMethod.Get, wrapperUrl);

            try
            {
                using var response = await ProbeClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
                if (!response.IsSuccessStatusCode)
                    return new SpaceProbeResult(SpaceStage.Unknown, null);

                var html = await response.Content.ReadAsStringAsync(ct);
                var stage = DetectSpaceStage(html);
                var appUrl = ExtractAppUrl(html);
                return new SpaceProbeResult(stage, appUrl);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new SpaceProbeResult(SpaceStage.Unknown, null);
            }
        }

        private static string FormatContext(IReadOnlyCollection<string> parts)
        {
            return parts.Count > 0 ? $" [{string.Join("; ", parts)}]" : string.Empty;
        }

        private static SpaceStage DetectSpaceStage(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return SpaceStage.Unknown;

            var lowered = html.ToLowerInvariant();

            if (lowered.Contains("&quot;stage&quot;:&quot;running") ||
                lowered.Contains(">running") ||
                lowered.Contains("class=\"cursor-pointer") && lowered.Contains("running"))
            {
                return SpaceStage.Running;
            }

            if (lowered.Contains("&quot;stage&quot;:&quot;sleeping") ||
                lowered.Contains("restart this space") ||
                lowered.Contains("this space is sleeping"))
            {
                return SpaceStage.Sleeping;
            }

            return SpaceStage.Unknown;
        }

        private static string? ExtractAppUrl(string html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var match = IframeSrcRegex.Match(html);
            if (match.Success)
                return WebUtility.HtmlDecode(match.Groups["src"].Value);

            string? TryExtractFromEncoded(string key)
            {
                var startIdx = html.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (startIdx < 0) return null;
                startIdx += key.Length;
                var endIdx = html.IndexOf("&quot;", startIdx, StringComparison.OrdinalIgnoreCase);
                if (endIdx <= startIdx) return null;
                var encoded = html.Substring(startIdx, endIdx - startIdx);
                return WebUtility.HtmlDecode(encoded);
            }

            var iframeSrc = TryExtractFromEncoded("&quot;iframeSrc&quot;:&quot;");
            if (!string.IsNullOrWhiteSpace(iframeSrc))
                return iframeSrc;

            var genericSrc = TryExtractFromEncoded("&quot;src&quot;:&quot;");
            if (!string.IsNullOrWhiteSpace(genericSrc) &&
                genericSrc.Contains(".hf.space", StringComparison.OrdinalIgnoreCase))
            {
                return genericSrc;
            }

            return null;
        }
    }
}
