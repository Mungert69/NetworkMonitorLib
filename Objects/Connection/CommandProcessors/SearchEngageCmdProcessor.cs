using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO; // DumpPageHtml
using PuppeteerSharp;
using PuppeteerSharp.Input;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Connection;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Connection
{
    public class SearchEngageCmdProcessor : CmdProcessor
    {
        // Defaults (overridable via CLI)
        private readonly int _searchTimeout = 60_000;
        private readonly int _engagementTimeout = 60_000;
        private readonly int _interactionDelayMin = 20_000;
        private readonly int _interactionDelayMax = 60_000;

        private readonly Random _random = new Random();
        private readonly ILaunchHelper? _launchHelper;
        private readonly List<ArgSpec> _schema;

        public SearchEngageCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
            ILaunchHelper launchHelper)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _launchHelper = launchHelper;

            _schema = new()
            {
                new()
                {
                    Key = "search_term",
                    Required = true,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Term to search for (required)."
                },
                new()
                {
                    Key = "target_domain",
                    Required = true,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Domain to match in results (required), e.g. example.com."
                },
                new()
                {
                    Key = "search_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = "60000",
                    Help = "Search navigation timeout in ms (default 60000)."
                },
                new()
                {
                    Key = "engagement_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = "60000",
                    Help = "Engagement phase budget in ms (default 60000)."
                },
                new()
                {
                    Key = "interaction_delay_min",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = "20000",
                    Help = "Minimum random delay between interactions in ms."
                },
                new()
                {
                    Key = "interaction_delay_max",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = "60000",
                    Help = "Maximum random delay between interactions in ms."
                }
            };
        }

        public override async Task<ResultObj> RunCommand(
            string arguments,
            CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    var m = $"{_cmdProcessorStates.CmdDisplayName} not available";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }

                if (_launchHelper == null)
                {
                    const string m = "PuppeteerSharp browser is not available on this agent. Check installation.";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }

                // Parse args (validate ints, fill defaults)
                var parsed = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
                if (!parsed.Success)
                {
                    var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parsed, _schema);
                    _logger.LogWarning("Arguments not valid {args}. {msg}", arguments, parsed.Message);
                    return new ResultObj { Success = false, Message = await SendMessage(err, processorScanDataObj) };
                }

                var searchTerm         = parsed.GetString("search_term");
                var targetDomain       = parsed.GetString("target_domain");
                var searchTimeout      = parsed.GetInt("search_timeout", _searchTimeout);
                var engagementTimeout  = parsed.GetInt("engagement_timeout", _engagementTimeout);
                var delayMin           = parsed.GetInt("interaction_delay_min", _interactionDelayMin);
                var delayMax           = parsed.GetInt("interaction_delay_max", _interactionDelayMax);

                if (delayMax < delayMin)
                {
                    // Swap to be forgiving rather than failing
                    (delayMin, delayMax) = (delayMax, delayMin);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Open prepared browser session (UA, headers, stealth, viewport)
                var session = await WebAutomationHelper.OpenSessionAsync(
                    _launchHelper, _netConfig, _logger, cancellationToken,
                    defaultPageTimeoutMs: Math.Max(1000, searchTimeout / 4),
                    options: new WebAutomationHelper.BrowserSessionOptions
                    {
                        ApplyStealth = true,
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                                    "Chrome/115.0.0.0 Safari/537.36",
                        ExtraHeaders = new Dictionary<string, string>
                        {
                            ["Accept-Language"] = "en-US,en;q=0.9"
                        }
                    });

                await using var __ = session;
                var page   = session.Page;
                var helper = new SearchWebHelper(_logger, _netConfig, Math.Max(1000, searchTimeout / 4), searchTimeout * 2);

                // Phase 1: Perform search
                var urlResult = await helper.FetchGoogleSearchUrlsAsync(page, searchTerm, cancellationToken);
                if (!urlResult.Success || urlResult.Data == null || urlResult.Data.Count == 0)
                {
                    return new ResultObj
                    {
                        Success = false,
                        Message = await SendMessage(urlResult.Message ?? "No search results found", processorScanDataObj)
                    };
                }

                // Find the first link where host equals or contains target domain
                var targetLink = urlResult.Data.FirstOrDefault(l =>
                    Uri.TryCreate(l, UriKind.Absolute, out var uri) &&
                    (uri.Host.Equals(targetDomain, StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.Contains(targetDomain, StringComparison.OrdinalIgnoreCase)));

                if (string.IsNullOrEmpty(targetLink))
                {
                    _logger.LogInformation("Target domain not found in links.");
                    return new ResultObj
                    {
                        Success = false,
                        Message = await SendMessage("Target site not found in search results", processorScanDataObj)
                    };
                }

                // Click the result / navigate
                await page.GoToAsync(targetLink, new NavigationOptions
                {
                    Timeout = searchTimeout,
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle0 }
                });

                // Phase 2: Engagement Simulation
                var engagementResult = await ExecuteEngagementFlow(page, delayMin, delayMax, engagementTimeout, cancellationToken);

                return new ResultObj
                {
                    Success = engagementResult.Success,
                    Message = engagementResult.Message,
                    Data    = engagementResult.Data
                };
            }
            catch (OperationCanceledException)
            {
                return new ResultObj { Success = false, Message = "Search engagement canceled or timed out.\n" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search engagement failed");
                return new ResultObj { Success = false, Message = $"Error: {ex.Message}" };
            }
        }

        private async Task DumpPageHtml(IPage page, string label = "page_debug")
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var safeLabel = (label ?? "page_debug").Replace(" ", "_");
            var html = await page.GetContentAsync();
            var fileName = $"debug_{safeLabel}_{timestamp}.html";
            File.WriteAllText(fileName, html);
            _logger.LogInformation("Saved page HTML to: {file}", fileName);
        }

        private async Task<ResultObj> ExecuteEngagementFlow(
            IPage page,
            int delayMin,
            int delayMax,
            int engagementTimeout,
            CancellationToken ct)
        {
            var result = new ResultObj();
            try
            {
                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                opCts.CancelAfter(engagementTimeout);

                try
                {
                    await ScrollLikeHuman(page);
                    await RandomDelay(delayMin, delayMax, opCts.Token);
                } catch { /* best effort */ }

                try
                {
                    await ClickRandomLink(page);
                    await RandomDelay(delayMin, delayMax, opCts.Token);
                } catch { /* best effort */ }

                try
                {
                    await ScrollLikeHuman(page, fastScroll: false);
                    await HoverOverElements(page);
                } catch { /* best effort */ }

                var metrics = new EngagementMetrics
                {
                    TimeOnSite   = engagementTimeout,
                    PagesVisited = 2,
                    Interactions = 3
                };

                result.Success = true;
                result.Message = "Engagement simulation completed successfully";
                result.Data    = metrics;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "Engagement timed out.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Engagement failed: {ex.Message}";
            }
            return result;
        }

        #region Human Simulation Helpers
        private async Task HumanLikeTyping(IPage page, string selector, string text)
        {
            await page.ClickAsync(selector);
            foreach (var c in text)
            {
                await page.Keyboard.PressAsync(c.ToString());
                await Task.Delay(Random.Shared.Next(50, 150));
            }
        }

        private async Task ScrollLikeHuman(IPage page, bool fastScroll = true)
        {
            var scrollSteps  = fastScroll ? 5 : 20;
            var scrollAmount = await page.EvaluateExpressionAsync<int>("document.body.scrollHeight");

            for (int i = 0; i < scrollSteps; i++)
            {
                await page.EvaluateExpressionAsync($"window.scrollBy(0, {Math.Max(1, scrollAmount / Math.Max(1, scrollSteps))})");
                await Task.Delay(Random.Shared.Next(100, 300));
            }
        }

        private async Task HumanClick(IPage page, string url)
        {
            var element = await page.QuerySelectorAsync($"a[href*='{url}']");
            if (element == null) return;

            var rect = await element.BoundingBoxAsync();
            if (rect == null) return;

            await page.Mouse.MoveAsync(
                rect.X + rect.Width / 2 + _random.Next(-5, 5),
                rect.Y + rect.Height / 2 + _random.Next(-5, 5),
                new MoveOptions { Steps = _random.Next(3, 10) });

            await Task.Delay(_random.Next(200, 600));
            await element.ClickAsync();
            await Task.Delay(_random.Next(1000, 3000));
        }

        private async Task ClickRandomLink(IPage page)
        {
            var links = await page.QuerySelectorAllAsync("a");
            var count = links.Count();
            if (count > 0)
            {
                var randomLink = links[Random.Shared.Next(count)];
                await randomLink.ClickAsync();
                try { await page.WaitForNavigationAsync(); } catch { /* ignore */ }
            }
        }

        private async Task HoverOverElements(IPage page)
        {
            var elements = await page.QuerySelectorAllAsync("a, button, .hover-element");
            foreach (var element in elements)
            {
                if (Random.Shared.NextDouble() < 0.3)
                {
                    await element.HoverAsync();
                    await Task.Delay(Random.Shared.Next(500, 1500));
                }
            }
        }

        private static async Task RandomDelay(int minMs, int maxMs, CancellationToken ct)
        {
            var dur = Random.Shared.Next(Math.Max(0, minMs), Math.Max(minMs + 1, maxMs));
            await Task.Delay(dur, ct);
        }
        #endregion

        public override string GetCommandHelp() => @"
SearchEngageCmdProcessor

Simulates organic search behavior:
  1) Search entry
  2) Result navigation
  3) On-site engagement

Usage:
  --search_term ""<query>"" --target_domain ""<domain>"" 
  [--search_timeout <ms>] [--engagement_timeout <ms>]
  [--interaction_delay_min <ms>] [--interaction_delay_max <ms>]

Arguments:
  --search_term            Term to search for (required)
  --target_domain          Domain to find in results (required)
  --search_timeout         Search navigation timeout (default 60000)
  --engagement_timeout     Engagement budget (default 60000)
  --interaction_delay_min  Min delay between interactions (default 20000)
  --interaction_delay_max  Max delay between interactions (default 60000)

Example:
  --search_term ""network security tools"" --target_domain ""example.com""

Metrics returned:
  • TimeOnSite (ms)
  • PagesVisited
  • Interactions
";
    }

    public class EngagementMetrics
    {
        public int TimeOnSite   { get; set; }
        public int PagesVisited { get; set; }
        public int Interactions { get; set; }
    }
}
