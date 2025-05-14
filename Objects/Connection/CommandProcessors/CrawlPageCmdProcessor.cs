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
    public class CrawlPageCmdProcessor : CmdProcessor
    {
        private int _microTimeout = 10000;
        private int _macroTimeout = 30000;

        public CrawlPageCmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
: base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {

        }
        public override async Task<ResultObj> RunCommand(string url, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
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
                cancellationToken.ThrowIfCancellationRequested();

                bool useHeadless = LaunchHelper.CheckDisplay(_logger,_netConfig.ForceHeadless);
                var lo = await LaunchHelper.GetLauncher(_netConfig.CommandPath, _logger, useHeadless);
         
                using (var browser = await Puppeteer.LaunchAsync(lo))
                using (var page = await browser.NewPageAsync())
                {
                    var extractContentTask = CrawlHelper.ExtractContentFromUrl(page, url, _netConfig, _logger, _microTimeout);
                    var extractTimeoutTask = Task.Delay(_macroTimeout, cancellationToken);
                    var extractCompletedTask = await Task.WhenAny(extractContentTask, extractTimeoutTask);
                    var contentResult = new TResultObj<string>();
                    if (extractCompletedTask == extractTimeoutTask)
                    {
                        _logger.LogWarning($"ExtractContentFromUrl timed out for URL: {url}");
                        contentResult.Message += ($"Timeout occurred while extracting content from {url}\n\n");
                        contentResult.Success = false;
                    }
                    else
                    {
                        contentResult = await extractContentTask;
                    }


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
The CrawlPageCmdProcessor uses Puppeteer to crawl a given webpage and extract its content. 
This processor handles cookie consent pop-ups and waits for the network to become idle before extracting the content, ensuring comprehensive page analysis.

### Usage:

**Basic Command**:
- Provide the URL of the webpage as the argument to crawl and extract its content.

### Features:

1. **Content Extraction**:
   Extracts the main content of the specified webpage after handling necessary browser interactions.

   Example:

arguments: https://example.com


2. **Cookie Consent Handling**:
Automatically detects and handles cookie consent pop-ups during navigation.

3. **Network Idle Wait**:
Waits for the network to become idle, ensuring that all required resources have loaded before extracting content.

4. **Dynamic Content**:
The processor uses Puppeteer to handle JavaScript-heavy pages, ensuring that dynamically loaded content is included.

### Examples:

1. **Crawl a Simple Webpage**:

arguments: https://example.com

Crawls `https://example.com` and extracts the content after handling any cookie consent pop-ups.

2. **Handle Consent and Extract Content**:

arguments: https://example.com/news

Handles cookie consent if present and extracts the content from the news page.

### Notes:

- **Network Idle Timeout**:
- The processor waits for the network to become idle with a timeout of 10 seconds. This ensures that all critical page elements are fully loaded.
- You can adjust the timeout in the `_timeout` field.

- **Error Handling**:
- If Puppeteer cannot navigate to the page or extract content, the processor will log an error and return a failure message.

- **Cookie Consent**:
- If the page navigates to a different URL after handling cookie consent, the processor will adapt and reprocess the new page.

### Troubleshooting:

1. **Error: 'Command not available'**:
- Ensure Puppeteer is installed and accessible in the configured command path.

2. **Navigation Issues**:
- Verify that the URL is valid and accessible from the agent.

3. **Incomplete Content**:
- Check if the page has dynamic elements that take longer to load than the timeout value.

### Summary:

The CrawlPageCmdProcessor simplifies webpage crawling by leveraging Puppeteer for dynamic and static content extraction. 
Just provide a valid URL, and the processor handles all interactions, including cookie consent and network idle states, to extract comprehensive page content.
";
        }


    }

}