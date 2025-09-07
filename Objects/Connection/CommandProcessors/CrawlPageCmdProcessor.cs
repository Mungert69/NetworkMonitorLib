using System;
using System.Collections.Generic;
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
    public class CrawlPageCmdProcessor : CmdProcessor
    {
        private readonly ILaunchHelper? _launchHelper;
        private readonly List<ArgSpec> _schema;

        private const int DefaultMicroTimeoutMs = 10_000; // per-step waits
        private const int DefaultMacroTimeoutMs = 59_000; // overall extraction budget

        public CrawlPageCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
            ILaunchHelper? launchHelper = null
        ) : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _launchHelper = launchHelper;

            _schema = new()
            {
                new()
                {
                    Key = "url",
                    Required = true,
                    IsFlag = false,
                    TypeHint = "url",
                    Help = "Target page URL to crawl (http/https)."
                },
                new()
                {
                    Key = "timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultMicroTimeoutMs.ToString(),
                    Help = "Micro timeout in ms for individual waits/operations."
                },
                new()
                {
                    Key = "macro_timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = DefaultMacroTimeoutMs.ToString(),
                    Help = "Overall extraction timeout in ms."
                }
                // Example future flag:
                // new() { Key = "headless", IsFlag = true, DefaultValue = "true", Help = "Force headless; pass --headless=false to disable." }
            };
        }

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
                    var m = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent. Try installing the Quantum Secure Agent or select an agent that has OpenSSL enabled.";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }
                if (_launchHelper == null)
                {
                    const string m = "PuppeteerSharp browser is not available on this agent. Check the installation completed successfully.";
                    _logger.LogWarning(m);
                    return new ResultObj { Success = false, Message = await SendMessage(m, processorScanDataObj) };
                }

                // Parse + validate args; fill defaults
                var parseResult = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
                if (!parseResult.Success)
                {
                    var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parseResult, _schema);
                    _logger.LogWarning("Arguments not valid {args}. {msg}", arguments, parseResult.Message);
                    return new ResultObj { Success = false, Message = await SendMessage(err, processorScanDataObj) };
                }

                var url          = parseResult.GetString("url"); // validated URL
                var microTimeout = parseResult.GetInt("timeout", DefaultMicroTimeoutMs);
                var macroTimeout = parseResult.GetInt("macro_timeout", DefaultMacroTimeoutMs);

                cancellationToken.ThrowIfCancellationRequested();

                bool useHeadless = _launchHelper.CheckDisplay(_logger, _netConfig.ForceHeadless);
                var launchOptions = await _launchHelper.GetLauncher(_netConfig.CommandPath, _logger, useHeadless);

                await using var browser = await Puppeteer.LaunchAsync(launchOptions);
                await using var page = await browser.NewPageAsync();

                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                opCts.CancelAfter(macroTimeout);

                // Start content extraction with a macro timeout
                var extractTask = CrawlHelper.ExtractContentFromUrl(page, url, _netConfig, _logger, microTimeout);
                var timeoutTask = Task.Delay(macroTimeout, opCts.Token);

                var completed = await Task.WhenAny(extractTask, timeoutTask);
                TResultObj<string> contentResult;

                if (completed == timeoutTask)
                {
                    _logger.LogWarning("ExtractContentFromUrl timed out for URL: {url}", url);
                    contentResult = new TResultObj<string>
                    {
                        Success = false,
                        Message = $"Timeout occurred while extracting content from {url}\n\n"
                    };
                }
                else
                {
                    contentResult = await extractTask;
                }

                if (contentResult.Success)
                {
                    return new ResultObj
                    {
                        Success = true,
                        Message = contentResult.Data ?? string.Empty
                    };
                }

                return new ResultObj
                {
                    Success = false,
                    Message = contentResult.Message ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                return new ResultObj { Success = false, Message = "Operation canceled or timed out.\n" };
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
The CrawlPageCmdProcessor uses Puppeteer to crawl a given webpage and extract its content.
It handles cookie consent and waits for the network to become idle to capture dynamic content.

Usage:
  --url <url> [--timeout <int>] [--macro_timeout <int>]
Required:
 only --url is required.
Examples:
  --url https://example.com
  --url https://example.com/news --timeout 15000 --macro_timeout 90000

Notes:
  • --timeout controls short per-step waits (default: 10000 ms).
  • --macro_timeout limits total extraction time (default: 59000 ms).
  • If navigation or extraction fails, a clear error is returned.";
    }
}
