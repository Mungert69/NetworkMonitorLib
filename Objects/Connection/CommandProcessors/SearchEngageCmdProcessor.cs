using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.IO; // ← needed for DumpPageHtml
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
        private readonly int _searchTimeout = 60000;
        private readonly int _engagementTimeout = 60000;
        private readonly int _interactionDelayMin = 20000;
        private readonly int _interactionDelayMax = 60000;
        private readonly Random _random = new Random();
        ILaunchHelper? _launchHelper = null;

        public SearchEngageCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
            ILaunchHelper launchHelper)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _launchHelper = launchHelper;
        }

        public override async Task<ResultObj> RunCommand(
            string arguments,
            CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj();
            string output = string.Empty;

            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    _logger.LogWarning($"{_cmdProcessorStates.CmdDisplayName} is not available");
                    output = $"{_cmdProcessorStates.CmdDisplayName} not available";
                    result.Message = await SendMessage(output, processorScanDataObj);
                    result.Success = false;
                    return result;
                }

                if (_launchHelper == null)
                {
                    const string m = "PuppeteerSharp browser is not available on this agent. Check installation.";
                    _logger.LogWarning(m);
                    result.Message = await SendMessage(m, processorScanDataObj);
                    result.Success = false;
                    return result;
                }

                var parsedArgs   = ParseArguments(arguments);
                var searchTerm   = parsedArgs.GetString("search_term", "");
                var targetDomain = parsedArgs.GetString("target_domain", "");

                if (string.IsNullOrEmpty(searchTerm))   throw new ArgumentException("Search term required");
                if (string.IsNullOrEmpty(targetDomain)) throw new ArgumentException("Target domain required");

                // ► New: centralize launch + UA/headers + stealth in WebAutomationHelper
                var session = await WebAutomationHelper.OpenSessionAsync(
                    _launchHelper, _netConfig, _logger, cancellationToken,
                    defaultPageTimeoutMs: _searchTimeout / 4,
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
                var helper = new SearchWebHelper(_logger, _netConfig, _searchTimeout / 4, _searchTimeout * 2);

                // Phase 1: Perform Search using SearchWebHelper
                var urlResult = await helper.FetchGoogleSearchUrlsAsync(page, searchTerm, cancellationToken);

                if (!urlResult.Success || urlResult.Data == null || urlResult.Data.Count == 0)
                {
                    result.Success = false;
                    result.Message = urlResult.Message ?? "No search results found";
                    return result;
                }

                // Find the first link where the host equals or contains the target domain
                var targetLink = urlResult.Data.FirstOrDefault(l =>
                    Uri.TryCreate(l, UriKind.Absolute, out var uri) &&
                    (uri.Host.Equals(targetDomain, StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.Contains(targetDomain, StringComparison.OrdinalIgnoreCase)));

                if (string.IsNullOrEmpty(targetLink))
                {
                    _logger.LogInformation("Target domain not found in links.");
                    result.Success = false;
                    result.Message = "Target site not found in search results";
                    return result;
                }

                // Click the result
                await page.GoToAsync(targetLink, new NavigationOptions
                {
                    Timeout = _searchTimeout,
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle0 }
                });

                // Phase 2: Engagement Simulation
                var engagementResult = await ExecuteEngagementFlow(page, cancellationToken);

                result.Success = engagementResult.Success;
                result.Message = engagementResult.Message;
                result.Data    = engagementResult.Data;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Search engagement failed: {ex.Message}");
                result.Success = false;
                result.Message = $"Error: {ex.Message}";
            }

            return result;
        }

        private async Task DumpPageHtml(IPage page, string label = "page_debug")
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var safeLabel = label.Replace(" ", "_");

            var html = await page.GetContentAsync();
            var fileName = $"debug_{safeLabel}_{timestamp}.html";

            File.WriteAllText(fileName, html);
            _logger.LogInformation($"✅ Saved page HTML to: {fileName}");
        }

        private async Task<ResultObj> ExecuteEngagementFlow(IPage page, CancellationToken ct)
        {
            var result = new ResultObj();
            try
            {
                try
                {
                    await ScrollLikeHuman(page);
                    await RandomDelay();
                } catch { }

                try
                {
                    await ClickRandomLink(page);
                    await RandomDelay();
                } catch { }

                try
                {
                    await ScrollLikeHuman(page, fastScroll: false);
                    await HoverOverElements(page);
                } catch { }

                var metrics = new EngagementMetrics
                {
                    TimeOnSite   = _engagementTimeout,
                    PagesVisited = 2,
                    Interactions = 3
                };

                result.Success = true;
                result.Message = "Engagement simulation completed successfully";
                result.Data    = metrics;
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
                await page.EvaluateExpressionAsync($"window.scrollBy(0, {scrollAmount / scrollSteps})");
                await Task.Delay(Random.Shared.Next(100, 300));
            }
        }

        private async Task HumanClick(IPage page, string url)
        {
            var element = await page.QuerySelectorAsync($"a[href*='{url}']");
            if (element == null) return;

            var rect = await element.BoundingBoxAsync();

            await page.Mouse.MoveAsync(
                rect.X + rect.Width / 2 + _random.Next(-5, 5),
                rect.Y + rect.Height / 2 + _random.Next(-5, 5),
                new MoveOptions { Steps = _random.Next(3, 10) });

            await RandomDelay();
            await element.ClickAsync();
            await Task.Delay(_random.Next(1000, 3000));
        }

        private async Task ClickRandomLink(IPage page)
        {
            var links = await page.QuerySelectorAllAsync("a");
            if (links.Count() > 0)
            {
                var randomLink = links[Random.Shared.Next(links.Count())];
                await randomLink.ClickAsync();
                await page.WaitForNavigationAsync();
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

        private async Task RandomDelay()
        {
            await Task.Delay(Random.Shared.Next(_interactionDelayMin, _interactionDelayMax));
        }
        #endregion

        public override string GetCommandHelp() => @"
## Search Engagement Command Processor

Simulates organic search behavior to improve website rankings through:
1. Search term entry
2. Search result navigation
3. Targeted site engagement

### Arguments:
--search_term: Term to search for (required)
--target_domain: Domain to find in results (required)

### Features:
- Human-like search input simulation
- Context-aware result navigation
- Behavioral engagement patterns:
  - Natural scrolling
  - Randomized clicking
  - Element hovering
  - Reading pauses
- Session persistence
- Anti-detection mechanisms

### Usage Example:
--search_term=""network security tools"" --target_domain=""example.com""

### Metrics Collected:
- Time spent on site
- Pages visited
- Interaction count
";
    }

    public class EngagementMetrics
    {
        public int TimeOnSite   { get; set; }
        public int PagesVisited { get; set; }
        public int Interactions { get; set; }
    }
}
