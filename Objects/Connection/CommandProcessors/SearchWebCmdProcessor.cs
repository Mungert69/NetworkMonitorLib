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

namespace NetworkMonitor.Connection
{
    public class SearchWebCmdProcessor : CmdProcessor
    {
        private const int DefaultMicroTimeoutMs = 10_000;
        private const int DefaultMacroTimeoutMs = 120_000;

        private int _microTimeout = DefaultMicroTimeoutMs;
        private int _macroTimeout = DefaultMacroTimeoutMs;

        private readonly IBrowserHost _browserHost;
        private readonly List<ArgSpec> _schema;

        public SearchWebCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
            IBrowserHost? browserHost = null)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _browserHost = browserHost ?? throw new ArgumentNullException(nameof(browserHost));

            _schema = new()
            {
                new()
                {
                    Key = "search_term",
                    Required = true,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Query to search for (required)."
                },
                new()
                {
                    Key = "return_only_urls",
                    Required = false,
                    IsFlag = true,
                    DefaultValue = "false",
                    Help = "If present (or true), returns only the URLs (no page extraction)."
                },
                new()
                {
                    Key = "micro_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultMicroTimeoutMs.ToString(),
                    Help = "Per-step timeout in ms (default 10000)."
                },
                new()
                {
                    Key = "macro_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultMacroTimeoutMs.ToString(),
                    Help = "Overall operation timeout in ms (default 120000)."
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
                    var m = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent. Try installing the Quantum Secure Agent or select an agent that has OpenSSL enabled.";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }

                // Parse args via schema (validates ints, normalizes flags, fills defaults)
                var parse = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
                if (!parse.Success)
                {
                    var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parse, _schema);
                    _logger.LogWarning("Arguments not valid {args}. {msg}", arguments, parse.Message);
                    return new ResultObj { Success = false, Message = await SendMessage(err, processorScanDataObj) };
                }

                var searchTerm = parse.GetString("search_term");
                var returnOnlyUrls = parse.GetBool("return_only_urls", false);
                _microTimeout = parse.GetInt("micro_timeout", DefaultMicroTimeoutMs);
                _macroTimeout = parse.GetInt("macro_timeout", DefaultMacroTimeoutMs);

                cancellationToken.ThrowIfCancellationRequested();

                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                opCts.CancelAfter(_macroTimeout);

                // Run the entire flow on a single shared, queued page
                var (ok, msg) = await _browserHost.RunWithPage(async page =>
                {
                    var helper = new SearchWebHelper(_logger, _netConfig, _microTimeout, _macroTimeout);

                    // Phase 1: Fetch URLs on the same page
                    await helper.StealthAsync(page);

                    var fetchUrlsTask = helper.FetchGoogleSearchUrlsAsync(page, searchTerm, opCts.Token);
                    var fetchTimeout = Task.Delay(_macroTimeout, opCts.Token);
                    var fetchWinner = await Task.WhenAny(fetchUrlsTask, fetchTimeout);

                    if (fetchWinner == fetchTimeout)
                    {
                        const string timeoutMsg = "FetchUrls operation timed out.";
                        _logger.LogWarning(timeoutMsg);
                        return (false, timeoutMsg);
                    }

                    var resultUrls = await fetchUrlsTask;
                    if (!resultUrls.Success || resultUrls.Data == null || resultUrls.Data.Count == 0)
                    {
                        var notFoundMsg = resultUrls.Message ?? "No search results found.";
                        return (false, notFoundMsg);
                    }

                    if (returnOnlyUrls)
                    {
                        return (true, string.Join("\n", resultUrls.Data));
                    }

                    // Phase 2: Visit each URL sequentially on the same page and extract content
                    var outputBuilder = new StringBuilder();

                    foreach (var url in resultUrls.Data)
                    {
                        opCts.Token.ThrowIfCancellationRequested();

                        try
                        {
                            var extractTask = CrawlHelper.ExtractContentFromUrl(page, url, _netConfig, _logger, _microTimeout);
                            var urlTimeout = Task.Delay(_macroTimeout, opCts.Token);
                            var winner = await Task.WhenAny(extractTask, urlTimeout);

                            if (winner == urlTimeout)
                            {
                                _logger.LogWarning("ExtractContentFromUrl timed out for URL: {url}", url);
                                outputBuilder.Append($"Timeout occurred while extracting content from {url}\n\n");
                                continue;
                            }

                            var contentResult = await extractTask;

                            if (contentResult.Success)
                            {
                                outputBuilder.Append($"Content from page {url}\nStart Content =>\n{contentResult.Data}\n<= End Content\n\n");
                            }
                            else
                            {
                                var m = string.IsNullOrWhiteSpace(contentResult.Message)
                                    ? "No content"
                                    : contentResult.Message;
                                outputBuilder.Append($"{m} for page {url}\n\n");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error extracting content from {url}", url);
                            outputBuilder.Append($"Error extracting content from {url}: {ex.Message}\n\n");
                        }
                    }

                    return (true, outputBuilder.ToString());
                }, opCts.Token);

                return new ResultObj
                {
                    Success = ok,
                    Message = await SendMessage(msg, processorScanDataObj)
                };
            }
            catch (OperationCanceledException)
            {
                return new ResultObj { Success = false, Message = "Search operation canceled or timed out.\n" };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error running {cmd} command.", _cmdProcessorStates.CmdName);
                return new ResultObj
                {
                    Success = false,
                    Message = $"Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}\n"
                };
            }
        }

        public override string GetCommandHelp() => @"
SearchWebCmdProcessor

Performs a Google search, collects result URLs, and (optionally) fetches page content.

Usage:
  --search_term ""<query>"" [--return_only_urls] [--micro_timeout <ms>] [--macro_timeout <ms>]

Arguments:
  --search_term        Query to search for (required).
  --return_only_urls   Presence-only flag: if set (or true), returns only URLs.
  --micro_timeout      Per-step timeout in ms (default 10000).
  --macro_timeout      Overall operation timeout in ms (default 120000).

Examples:
  --search_term ""latest AI research"" --return_only_urls
  --search_term ""best cybersecurity practices"" --macro_timeout 180000

Notes:
  • When --return_only_urls is absent/false, each URL is visited and content is extracted.
  • Uses stealth & realistic delays via SearchWebHelper to reduce bot detection.";

        // (Optional) Keep for debugging unexpected pages while iterating locally
        private async Task DumpPageHtml(IPage page, string label = "page_debug")
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var safeLabel = (label ?? "page_debug").Replace(" ", "_");
            var html = await page.GetContentAsync();
            var fileName = $"debug_{safeLabel}_{timestamp}.html";
            File.WriteAllText(fileName, html);
            _logger.LogInformation("Saved page HTML to: {file}", fileName);
        }

        // Simple HTML summary utility (unchanged)
        private string SummarizePageContent(string pageContent)
        {
            var summary = new StringBuilder();
            summary.AppendLine("Summary of unexpected page content:");

            var titleMatch = Regex.Match(pageContent, @"<title>(.*?)<\/title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (titleMatch.Success) summary.AppendLine($"Title: {titleMatch.Groups[1].Value}");

            var errorMessageMatch = Regex.Match(pageContent, @"<div.*?>(.*?)<\/div>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (errorMessageMatch.Success) summary.AppendLine($"Message: {errorMessageMatch.Groups[1].Value}");

            return summary.ToString();
        }
    }
}
