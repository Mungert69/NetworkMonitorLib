using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using System.Runtime.InteropServices;
using NetworkMonitor.Service.Services.OpenAI;
using System.Collections.Generic;


namespace NetworkMonitor.Connection
{
    public class SearchWebCmdProcessor : CmdProcessor
    {
        private int _microTimeout = 10000;
        private int _macroTimeout = 120000;

        public SearchWebCmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
: base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {

        }
        public override async Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj();
            string output = "";
            var resultUrls = new TResultObj<List<string>>();
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    _logger.LogWarning($" Warning : {_cmdProcessorStates.CmdDisplayName} is not enabled or installed on this agent.");
                    output = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent. Try installing the Quantum Secure Agent or select an agent that has Openssl enabled.\n";
                    result.Message = await SendMessage(output, processorScanDataObj);
                    result.Success = false;
                    return result;
                }

                var parsedArgs = base.ParseArguments(arguments);
                string searchTerm = parsedArgs.GetString("search_term", "");
                if (string.IsNullOrEmpty(searchTerm))
                {
                    result.Success = false;
                    result.Message = " Search term is empty can not search";
                    return result;
                }

                bool returnOnlyUrls = parsedArgs.GetBool("return_only_urls", false);
                bool useHeadless = LaunchHelper.CheckDisplay(_logger, _netConfig.ForceHeadless);
                var lo = await LaunchHelper.GetLauncher(_netConfig.CommandPath, _logger, useHeadless);


                using (var browser = await Puppeteer.LaunchAsync(lo))
                {
                    // Fetch URLs using a single page
                    using (var fetchPage = await browser.NewPageAsync())
                    {
                        var fetchUrlsTask = FetchUrls(fetchPage, searchTerm);
                        var timeoutTask = Task.Delay(_macroTimeout, cancellationToken);
                        var completedTask = await Task.WhenAny(fetchUrlsTask, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            _logger.LogWarning("FetchUrls operation timed out.");
                            result.Success = false;
                            result.Message = "FetchUrls operation timed out.";
                            return result;
                        }

                        resultUrls = await fetchUrlsTask;

                        if (returnOnlyUrls)
                        {
                            if (resultUrls.Success && resultUrls.Data != null && resultUrls.Data.Count > 0)
                            {
                                result.Message = string.Join("\n", resultUrls.Data);
                                result.Success = true;
                                return result;
                            }
                            else
                            {
                                result.Success = false;
                                result.Message = resultUrls.Message;
                                return result;
                            }
                        }
                    }

                    // Process each URL with a new page
                    if (resultUrls.Success && resultUrls.Data != null && resultUrls.Data.Count > 0)
                    {
                        var outputBuilder = new StringBuilder();

                        foreach (var url in resultUrls.Data)
                        {
                            // Create a new page for each URL
                            using (var urlPage = await browser.NewPageAsync())
                            {
                                try
                                {
                                    var extractContentTask = CrawlHelper.ExtractContentFromUrl(urlPage, url, _netConfig, _logger, _microTimeout);
                                    var extractTimeoutTask = Task.Delay(_macroTimeout, cancellationToken);
                                    var extractCompletedTask = await Task.WhenAny(extractContentTask, extractTimeoutTask);

                                    if (extractCompletedTask == extractTimeoutTask)
                                    {
                                        _logger.LogWarning($"ExtractContentFromUrl timed out for URL: {url}");
                                        outputBuilder.Append($"Timeout occurred while extracting content from {url}\n\n");
                                        continue; // Skip to the next URL
                                    }

                                    var contentResult = await extractContentTask;

                                    if (contentResult.Success)
                                    {
                                        outputBuilder.Append($"Content from page {url} \nStart Content =>\n{contentResult.Data}\n<=End Content\n\n");
                                    }
                                    else
                                    {
                                        outputBuilder.Append($"No content for page {url}\n\n");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"Error extracting content from {url}: {ex.Message}");
                                    outputBuilder.Append($"Error extracting content from {url}: {ex.Message}\n\n");
                                }
                            }
                        }

                        result.Message = outputBuilder.ToString();
                        result.Success = true;
                    }
                    else
                    {
                        result.Success = false;
                        result.Message = resultUrls.Message;
                    }
                    return result;

                }

            }
            catch (Exception e)
            {
                _logger.LogError($"Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}");
                output += $"Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}\n";
                result.Success = false;
            }

            result.Message = output;
            return result;
        }
        public override string GetCommandHelp()
        {
            return @"
The SearchWebCmdProcessor executes a web search query using Google Search. It extracts relevant URLs from the results and optionally fetches page content.

### Usage:
**Basic Command:**
- Pass a search term as an argument to retrieve search results.

### Supported Arguments:
1. **search_term** (string): The query to search for.
   - Example: --search_term ""network security best practices""
2. **return_only_urls** (bool, optional): If true, returns only URLs.
   - Example: --return_only_urls

### Features:
1. **Real-Time Web Search**:
   - Uses Puppeteer to perform a live Google search.
   - Extracts URLs from search results dynamically.
   
2. **Browser Simulation**:
   - Mimics human interaction to bypass bot detection.
   - Introduces random delays for realistic browsing behavior.

3. **Content Extraction** (if return_only_urls is false or missing):
   - Visits extracted URLs and retrieves page content.
   - Returns extracted text for further analysis.

4. **Fallback Mechanism**:
   - If Puppeteer fails due to CAPTCHA or bot detection, switches to Google Custom Search API.

### Examples:
1. **Fetch URLs Only:**
   ```
   --search_term ""latest AI research"" --return_only_urls
   ```
   Returns URLs from Google Search results.

2. **Fetch Content from Results:**
   ```
   --search_term ""best cybersecurity practices""
   ```
   Returns URLs along with extracted content from each page.

### Troubleshooting:
- Ensure the agent can access Google Search.
- For blocked searches, consider using the Google Custom Search API.

### Summary:
The SearchWebCmdProcessor is designed for retrieving search results dynamically and extracting relevant data. Ideal for research, intelligence gathering, and automated information retrieval.
";
        }

        static async Task RandomDelay(int min, int max)
        {
            var random = new Random();
            int delay = random.Next(min, max);
            await Task.Delay(delay);
        }

        private async Task<TResultObj<List<string>>> FetchUrls(IPage page, string searchTerm)
        {
            string[] urls;
            var result = new TResultObj<List<string>>();

            _logger.LogInformation($"Navigating to Google Search with term: {searchTerm}");

            await page.GoToAsync($"https://www.google.com/search?q={Uri.EscapeDataString(searchTerm)}", new NavigationOptions { Timeout = _microTimeout });

            _logger.LogInformation("Waiting for search results to load...");
            await RandomDelay(5000, 10000); // Random delay between 5s and 10s

            string pageContent = await page.GetContentAsync(); // Always fetch page content
            try
            {
                // Check for CAPTCHA or unusual traffic message
                if (pageContent.Contains("unusual traffic from your computer network") ||
                    pageContent.Contains("please verify you are not a robot"))
                {
                    _logger.LogWarning("Google detected unusual traffic or CAPTCHA.");
                    _logger.LogInformation("Falling back to Google Custom Search JSON API.");

                    return await FetchUrlsFromGoogleApi(searchTerm);
                }

                // Wait for the search results container to be visible
                await page.WaitForFunctionAsync(
                    "() => document.querySelectorAll('.g').length > 0",
                    new WaitForFunctionOptions { Timeout = _microTimeout }
                );

                _logger.LogInformation("Search results loaded.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Search results not found or timed out. Falling back to API. Error: {ex.Message}");
                return await FetchUrlsFromGoogleApi(searchTerm);
            }

            await RandomDelay(1500, 3000); // Random delay between 1.5s and 3s

            // Extract URLs from search results

            try
            {
                urls = await page.EvaluateFunctionAsync<string[]>(
                    "() => Array.from(document.querySelectorAll('.g a')).map(link => link.href).filter(href => href.includes('http') && !href.includes('webcache'))"
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error extracting URLs: {ex.Message}");
                return await FetchUrlsFromGoogleApi(searchTerm);
            }

            if (urls == null || urls.Length == 0)
            {
                _logger.LogInformation("No URLs extracted. Falling back to API.");
                return await FetchUrlsFromGoogleApi(searchTerm);
            }


            result.Data = urls.ToList();
            result.Success = true;
            if (result.Data == null || result.Data.Count == 0)
            {
                result.Message = "Failed to fetch any urls";
                result.Success = false;
            }

            return result;
        }

        // Fallback to Google Custom Search API
        private async Task<TResultObj<List<string>>> FetchUrlsFromGoogleApi(string searchTerm)
        {
            var result = new TResultObj<List<string>>();

            if (_netConfig.GoogleSearchApiCxID == "Not Set" || _netConfig.GoogleSearchApiKey == "Not set")
            {
                result.Message = "To use the google search api as a fallback for the Search Web Cmd Processor you need to set GoogleSearchApiKey and GoogleSearchApiCxID with the values you have set at https://console.cloud.google.com/apis/api/customsearch.googleapis.com";
                result.Success = false;
                return result;
            }
            string url = $"https://www.googleapis.com/customsearch/v1?q={Uri.EscapeDataString(searchTerm)}&key={_netConfig.GoogleSearchApiKey}&cx={_netConfig.GoogleSearchApiCxID}";
            _logger.LogInformation($" Using url for Google Search Api : {url}");
            using (var httpClient = new HttpClient())
            {
                try
                {
                    var response = await httpClient.GetStringAsync(url);

                    // Deserialize the response
                    var searchResponse = JsonSerializer.Deserialize<GoogleSearchResponse>(response);

                    if (searchResponse?.Items != null && searchResponse.Items.Count > 0)
                    {
                        var urls = searchResponse.Items.Select(item => item.Link).ToList();
                        result.Data = urls;
                        result.Success = true;
                        return result;
                    }

                    result.Success = false;
                    result.Message = "Failed to fetch any urls";
                }
                catch (Exception ex)
                {
                    result.Message = $"Error fetching results from Google API: {ex.Message}";
                    result.Success = false;
                    _logger.LogError(result.Message);

                }
                return result;
            }

        }



        private string SummarizePageContent(string pageContent)
        {
            // Extract key elements from the page content for summary
            var summary = new StringBuilder();
            summary.AppendLine("Summary of unexpected page content:");

            // Example: Extract title or main message
            var titleMatch = Regex.Match(pageContent, @"<title>(.*?)<\/title>");
            if (titleMatch.Success)
            {
                summary.AppendLine($"Title: {titleMatch.Groups[1].Value}");
            }

            // Example: Check for any error messages
            var errorMessageMatch = Regex.Match(pageContent, @"<div.*?>(.*?)<\/div>");
            if (errorMessageMatch.Success)
            {
                summary.AppendLine($"Message: {errorMessageMatch.Groups[1].Value}");
            }

            return summary.ToString();
        }
    }

    public class GoogleSearchResponse
    {
        [JsonPropertyName("items")]
        public List<SearchResultItem> Items { get; set; }
    }

    public class SearchResultItem
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("link")]
        public string Link { get; set; }

        [JsonPropertyName("snippet")]
        public string Snippet { get; set; }

        // Include other properties as needed
    }

}