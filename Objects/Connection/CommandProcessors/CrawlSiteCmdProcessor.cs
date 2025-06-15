using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Connection;
using NetworkMonitor.Utils;
using System.Xml.Linq;
using System.IO;
using System.Threading;
using PuppeteerSharp;
using NetworkMonitor.Service.Services.OpenAI;

namespace NetworkMonitor.Connection
{
    public class CrawlSiteCmdProcessor : CmdProcessor
    {
        private int _timeout = 10000;
        private int _simTimeout = 120000;
        private static readonly Random _random = new Random();


        public CrawlSiteCmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
: base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {

        }
        public override async Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj();
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    _logger.LogWarning($" Warning : {_cmdProcessorStates.CmdDisplayName} is not enabled or installed on this agent.");
                    var output = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent. Try installing the Quantum Secure Agent or select an agent that has Openssl enabled.\n";
                    result.Message = await SendMessage(output, processorScanDataObj);
                    result.Success = false;
                    return result;
                }

                var parsedArgs = ParseCommandLineArguments(arguments, 3, 10); // Defaults: maxDepth=3, maxPages=10
                int maxDepth = parsedArgs.MaxDepth;
                int maxPages = parsedArgs.MaxPages;

                // Call the CrawlSite method
                var contentResult = await CrawlSite(parsedArgs.Url, maxPages, maxDepth);
                cancellationToken.ThrowIfCancellationRequested();

                if (contentResult.Success)
                {
                    result.Success = true;
                    result.Message = contentResult.Data ?? "";
                }
                else
                {
                    result.Success = false;
                    result.Message = contentResult.Message ?? "";
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}");
                result.Message = $"Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}\n";
                result.Success = false;
            }
            return result;
        }

        public override string GetCommandHelp()
        {
            return @"
The CrawlSiteCmdProcessor is designed to simulate user browsing behavior on a website, driving site traffic by mimicking human interactions such as clicking internal links, scrolling, and navigating between pages. 

### Usage:

**Basic Command**:
- Provide the starting URL and optional arguments for maximum crawl depth and page limits.

### Supported Arguments:
1. **--url** (string): The starting URL of the website to crawl. Defaults to the Frontend URL.
   - Example: `--url https://example.com`

2. **--max_depth** (int): The maximum depth of link traversal. Defaults to 3.
   - Example: `--max_depth 5`

3. **--max_pages** (int): The maximum number of pages to visit during the crawl. Defaults to 10.
   - Example: `--max_pages 20`

### Features:

1. **Simulated User Interaction**:
   - Mimics real user behavior, such as scrolling, clicking internal links, and navigating between pages.

2. **Cookie Consent Handling**:
   - Automatically detects and handles cookie consent pop-ups encountered during the crawl.

3. **Dynamic Page Content**:
   - Extracts dynamically loaded content using Puppeteer, ensuring all JavaScript-rendered elements are included.

4. **Internal Link Extraction**:
   - Identifies and follows internal links on the site, avoiding external or third-party links.

5. **Randomized User Behavior**:
   - Introduces random delays and shuffles link traversal order to emulate human browsing patterns.

6. **Google Analytics Detection**:
   - Logs when Google Analytics scripts are loaded during the crawl.

### Examples:

1. **Basic Site Crawl**:

--url https://example.com --max_depth 3 --max_pages 10

Starts at `https://example.com`, traversing up to 3 link levels and visiting a maximum of 10 pages.

2. **Crawl with Extended Depth**:

--url https://example.com --max_depth 5 --max_pages 20

Explores the site up to 5 link levels deep and visits up to 20 pages.

3. **Default Crawl**:

No arguments provided.

Crawls the default Frontend URL, with a depth of 3 and a limit of 10 pages.

### Notes:

- **User Agent Spoofing**:
- Randomly selects a user agent string and modifies browser properties to bypass bot detection.

- **Headless or GUI Mode**:
- Runs in headless mode unless a GUI environment is detected.

- **Cookie Management**:
- Saves and applies cookies between pages for consistent session behavior.

- **Timeout Settings**:
- The processor waits for a network idle state with a timeout of 10 seconds. Adjust `_timeout` for longer or shorter waits.

- **Error Handling**:
- Logs errors encountered during navigation or interaction with pages.

### Troubleshooting:

1. **Error: 'Command not available'**:
- Ensure Puppeteer is installed and accessible in the configured command path.

2. **Incomplete Crawls**:
- Verify that the starting URL is valid and reachable from the agent.

3. **Blocked by Anti-Bot Mechanisms**:
- Use a different user agent or adjust crawl behavior if the site blocks bots.

### Summary:

The CrawlSiteCmdProcessor is ideal for simulating user browsing behavior, extracting site content, and generating traffic. It efficiently handles dynamic content, internal link navigation, and cookie consent, providing a realistic simulation of user activity.
";
        }
        private (string Url, int MaxDepth, int MaxPages) ParseCommandLineArguments(string arguments, int defaultDepth, int defaultPages)
        {
            string url = AppConstants.FrontendUrl;
            int maxDepth = defaultDepth;
            int maxPages = defaultPages;

            string[] tokens = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tokens.Length; i++)
            {
                switch (tokens[i])
                {
                    case "--url":
                        if (i + 1 < tokens.Length && !tokens[i + 1].StartsWith("--"))
                        {
                            url = tokens[++i];
                        }
                        break;
                    case "--max_depth":
                        if (i + 1 < tokens.Length && int.TryParse(tokens[++i], out int depth))
                        {
                            maxDepth = depth;
                        }
                        break;
                    case "--max_pages":
                        if (i + 1 < tokens.Length && int.TryParse(tokens[++i], out int pages))
                        {
                            maxPages = pages;
                        }
                        break;
                }
            }

            return (url, maxDepth, maxPages);
        }

        private async Task<TResultObj<string>> CrawlSite(string startUrl, int maxPages, int maxDepth)
        {

            var result = new TResultObj<string>();
            var visitedUrls = new HashSet<string>();
            var contentBuilder = new StringBuilder();
            var crawlQueue = new Queue<(string Url, int Depth)>();
            crawlQueue.Enqueue((startUrl, 0));
            bool isCookieClicked = false;
            var userAgent = UserAgents.GetRandomUserAgent();
            var platform = UserAgents.GetPlatformFromUserAgent(userAgent);
            string plugins = UserAgents.GetPluginsForUserAgent(userAgent);

            PuppeteerSharp.CookieParam[] storedCookies = Array.Empty<PuppeteerSharp.CookieParam>();

            bool useHeadless = LaunchHelper.CheckDisplay(_logger,_netConfig.ForceHeadless);

            var lo = await LaunchHelper.GetLauncher(_netConfig.CommandPath, _logger, useHeadless);
            using (var browser = await Puppeteer.LaunchAsync(lo))
            using (var page = await browser.NewPageAsync())
            {
                page.Response += (sender, e) =>
                {
                    if (e.Response.Url.Contains("google-analytics"))
                    {
                        _logger.LogInformation($"Google Analytics script loaded: {e.Response.Url}");
                    }
                };

                try
                {
                    await page.EvaluateFunctionOnNewDocumentAsync($@"() => {{
                Object.defineProperty(navigator, 'webdriver', {{ get: () => false }});
                Object.defineProperty(navigator, 'languages', {{ get: () => ['en-US', 'en'] }});
                Object.defineProperty(navigator, 'platform', {{ get: () => '{platform}' }});
                Object.defineProperty(navigator, 'plugins', {{
                    get: () => {plugins}
                }});
            }}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error injecting script on new document: {ex.Message}");
                }

                await page.SetUserAgentAsync(userAgent);
                await page.SetJavaScriptEnabledAsync(true);

                string? previousUrl = null;
                while (crawlQueue.Count > 0 && visitedUrls.Count < maxPages)
                {
                    var (currentUrl, currentDepth) = crawlQueue.Dequeue();

                    if (visitedUrls.Contains(currentUrl) || currentDepth > maxDepth)
                        continue;

                    visitedUrls.Add(currentUrl);

                    _logger.LogInformation($"Visiting {currentUrl} at depth {currentDepth}");

                    try
                    {
                        // Apply previously stored cookies
                        if (storedCookies.Any())
                        {
                            await page.SetCookieAsync(storedCookies.ToArray());
                        }

                        var extraHeaders = new Dictionary<string, string>();
                        if (previousUrl != null)
                        {
                            if (Uri.TryCreate(previousUrl, UriKind.Absolute, out _))
                            {
                                extraHeaders["Referer"] = previousUrl;
                            }
                        }
                        await page.SetExtraHttpHeadersAsync(extraHeaders);

                        // Navigate to the URL with a timeout (Puppeteer's built-in timeout)
                        await page.GoToAsync(currentUrl, new NavigationOptions { Timeout = _timeout });

                        // Wait for network idle with a timeout (built-in timeout)
                        await CrawlHelper.WaitNetIdle(page, _timeout, _logger);

                        // Handle cookie consent with a custom timeout
                        if (!isCookieClicked)
                        {
                            var handleCookieTask = CrawlHelper.HandleCookieConsent(page, _logger, _timeout);
                            var handleCookieTimeoutTask = Task.Delay(_timeout); // Custom timeout
                            var handleCookieCompletedTask = await Task.WhenAny(handleCookieTask, handleCookieTimeoutTask);

                            if (handleCookieCompletedTask == handleCookieTimeoutTask)
                            {
                                _logger.LogWarning($"Handling cookie consent timed out for {currentUrl}.");
                            }
                            else
                            {
                                await handleCookieTask;
                                isCookieClicked = true;
                            }
                        }

                        // Simulate user interaction with a custom timeout
                        var simulateInteractionTask = CrawlHelper.SimulateUserInteraction(page, _logger, _simTimeout);
                        var simulateInteractionTimeoutTask = Task.Delay(_simTimeout + 5000); // add 5s to the expected time for the Sim to run
                        var simulateInteractionCompletedTask = await Task.WhenAny(simulateInteractionTask, simulateInteractionTimeoutTask);

                        if (simulateInteractionCompletedTask == simulateInteractionTimeoutTask)
                        {
                            _logger.LogWarning($"Simulating user interaction timed out for {currentUrl}.");
                        }
                        else
                        {
                            var resultContent = await simulateInteractionTask;
                            contentBuilder.AppendLine(resultContent);
                        }

                        // Extract links with a custom timeout
                        var extractLinksTask = CrawlHelper.ExtractLinks(page, currentUrl, _logger);
                        var extractLinksTimeoutTask = Task.Delay(_timeout); // Custom timeout
                        var extractLinksCompletedTask = await Task.WhenAny(extractLinksTask, extractLinksTimeoutTask);

                        if (extractLinksCompletedTask == extractLinksTimeoutTask)
                        {
                            _logger.LogWarning($"Link extraction timed out for {currentUrl}.");
                        }
                        else
                        {
                            var resultLinks = await extractLinksTask;

                            if (resultLinks.Success && resultLinks.Data != null)
                            {
                                var links = resultLinks.Data;

                                // Randomize and enqueue links
                                var shuffledLinks = links.OrderBy(_ => Guid.NewGuid());
                                var limitedLinks = shuffledLinks.Take(_random.Next(3, Math.Min(8, links.Count())));

                                foreach (var link in limitedLinks)
                                {
                                    if (!visitedUrls.Contains(link) && CrawlHelper.IsInternalLink(startUrl, link))
                                    {
                                        crawlQueue.Enqueue((link, currentDepth + 1));
                                    }
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"Link extraction failed for {currentUrl}: {resultLinks.Message}");
                            }
                        }

                        // Save cookies for the next page
                        storedCookies = await page.GetCookiesAsync();
                        previousUrl = currentUrl;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing {currentUrl}: {ex.Message}");
                    }

                    // Wait for a random delay to mimic human browsing
                    await CrawlHelper.RandomDelay(5000, 10000);
                }
            }

            _logger.LogInformation("Site crawl completed.");
            return new TResultObj<string>() { Success = true, Message = "Crawl completed successfully.", Data = contentBuilder.ToString() };
        }

    }
}