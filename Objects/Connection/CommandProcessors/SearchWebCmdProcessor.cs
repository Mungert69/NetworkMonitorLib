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

        private const int ResultPageBudgetMs = 15_000;
        private const int MaxResultPages = 3;

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
                var microTimeout = parse.GetInt("micro_timeout", DefaultMicroTimeoutMs);
                var macroTimeout = parse.GetInt("macro_timeout", DefaultMacroTimeoutMs);

                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                opCts.CancelAfter(macroTimeout);

                TResultObj<List<string>> resultUrls;
                try
                {
                    resultUrls = await _browserHost.RunWithPage(async page =>
                    {
                        var helper = new SearchWebHelper(_logger, _netConfig, microTimeout, macroTimeout);
                        await helper.StealthAsync(page).WaitAsync(opCts.Token);
                        return await helper.FetchGoogleSearchUrlsAsync(page, searchTerm, opCts.Token)
                            .WaitAsync(opCts.Token);
                    }, opCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new ResultObj
                    {
                        Success = false,
                        Message = await SendMessage("Search timed out before finding any URLs.", processorScanDataObj)
                    };
                }

                if (!resultUrls.Success || resultUrls.Data == null || resultUrls.Data.Count == 0)
                {
                    return new ResultObj
                    {
                        Success = false,
                        Message = await SendMessage(resultUrls.Message ?? "No search results found.", processorScanDataObj)
                    };
                }

                var urls = resultUrls.Data;
                if (returnOnlyUrls)
                {
                    return new ResultObj
                    {
                        Success = true,
                        Message = await SendMessage(string.Join("\n", urls), processorScanDataObj)
                    };
                }

                var output = new StringBuilder();
                output.AppendLine("Search result URLs:");
                foreach (var url in urls)
                {
                    output.AppendLine(url);
                }

                var extractedCount = 0;
                foreach (var url in urls.Take(MaxResultPages))
                {
                    try
                    {
                        var content = await _browserHost.RunWithPage(async page =>
                        {
                            using var pageCts = CancellationTokenSource.CreateLinkedTokenSource(opCts.Token);
                            pageCts.CancelAfter(ResultPageBudgetMs);
                            return await CrawlHelper.ExtractContentFromUrl(page, url, _netConfig, _logger, microTimeout)
                                .WaitAsync(pageCts.Token);
                        }, opCts.Token);

                        if (content.Success)
                        {
                            extractedCount++;
                            output.AppendLine($"\nContent from page {url}\nStart Content =>\n{content.Data}\n<= End Content");
                        }
                        else
                        {
                            output.AppendLine($"\nCould not extract {url}: {content.Message}");
                        }
                    }
                    catch (OperationCanceledException) when (opCts.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        output.AppendLine($"\nPage extraction timed out for {url}.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not extract search result {Url}", url);
                        output.AppendLine($"\nCould not extract {url}.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (opCts.IsCancellationRequested)
                {
                    output.AppendLine("\nThe overall search time budget expired. Increase macro_timeout (milliseconds) if you need more time within this search.");
                }
                if (extractedCount < urls.Count)
                {
                    output.AppendLine("For more detail, use run_crawl_page with a relevant URL above.");
                }

                return new ResultObj { Success = true, Message = await SendMessage(output.ToString(), processorScanDataObj) };
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
  • When --return_only_urls is absent/false, up to three URLs are visited for content extraction.
  • Each page extraction gets up to 15 seconds. Results include URLs and any completed extracts.
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
