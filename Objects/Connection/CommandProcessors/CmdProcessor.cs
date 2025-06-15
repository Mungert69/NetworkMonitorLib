using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
using System.Text.Json;
using System.Text;

namespace NetworkMonitor.Connection
{
    public interface ICmdProcessor : IDisposable
    {
        Task Scan();
        Task CancelScan();
        Task<ResultObj> CancelCommand(string messageId);
        Task<ResultObj> QueueCommand(CancellationTokenSource cancellationToken, ProcessorScanDataObj processorScanDataObj);
        Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null);
        Task<ResultObj> PublishCommandHelp(ProcessorScanDataObj processorScanDataObj);
        bool UseDefaultEndpoint { get; set; }
        string GetCommandHelp();
    }
    public abstract class CmdProcessor : ICmdProcessor
    {
        protected readonly ILogger _logger;
        protected readonly ILocalCmdProcessorStates _cmdProcessorStates;
        protected readonly IRabbitRepo _rabbitRepo;
        protected readonly NetConnectConfig _netConfig;
        protected string _rootFolder; // the folder to read and write files to.
        protected CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentQueue<CommandTask> _currentQueue;
        private readonly SemaphoreSlim _semaphore;
        protected string _frontendUrl = AppConstants.FrontendUrl;
        private readonly ConcurrentDictionary<string, CommandTask> _runningTasks
    = new ConcurrentDictionary<string, CommandTask>();

        public bool UseDefaultEndpoint { get => _cmdProcessorStates.UseDefaultEndpointType; set => _cmdProcessorStates.UseDefaultEndpointType = value; }
#pragma warning disable CS8618
        public CmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
        {
            _logger = logger;
            _cmdProcessorStates = cmdProcessorStates;
            _rabbitRepo = rabbitRepo;
            _netConfig = netConfig;
            _rootFolder = netConfig.CommandPath;
            _cmdProcessorStates.OnStartScanAsync += Scan;
            _cmdProcessorStates.OnCancelScanAsync += CancelScan;
            _cmdProcessorStates.OnAddServicesAsync += AddServices;
            _currentQueue = new ConcurrentQueue<CommandTask>();
            _semaphore = new SemaphoreSlim(5);
            _ = StartQueueProcessorAsync();

        }
#pragma warning restore CS8618

        private async Task StartQueueProcessorAsync()
        {
            while (true) // Keep processing tasks indefinitely
            {
                await ProcessQueueAsync();
                await Task.Delay(1000);
            }
        }


        public virtual void Dispose()
        {
            _cmdProcessorStates.OnStartScanAsync -= Scan;
            _cmdProcessorStates.OnCancelScanAsync -= CancelScan;
            _cmdProcessorStates.OnAddServicesAsync -= AddServices;
            _cancellationTokenSource?.Dispose();
        }

        public virtual async Task Scan()
        {

            _logger.LogWarning($" Warning : {_cmdProcessorStates.CmdName} Scan Command is not enabled or installed on this agent.");
            var output = $"The {_cmdProcessorStates.CmdDisplayName}  Scan Command is not available on this agent. Try using another agent.\n";
            _cmdProcessorStates.IsSuccess = false;
            _cmdProcessorStates.IsRunning = false;
            await SendMessage(output, null);



        }

        public virtual async Task AddServices()
        {
            _logger.LogWarning($" Warning : {_cmdProcessorStates.CmdName} Add Services command is not enabled or installed on this agent.");
            var output = $"{_cmdProcessorStates.CmdDisplayName} Add Services command is not available on this agent. Try using another agent.\n";
            _cmdProcessorStates.IsSuccess = false;
            _cmdProcessorStates.IsRunning = false;
            await SendMessage(output, null);
        }

        public virtual async Task CancelScan()
        {
            if (!_cmdProcessorStates.IsCmdAvailable)
            {
                _logger.LogWarning($" Warning : {_cmdProcessorStates.CmdName} command is not enabled or installed on this agent.");
                var output = $"The {_cmdProcessorStates.CmdDisplayName} command is not available on this agent. Try using another agent.\n";
                _cmdProcessorStates.IsSuccess = false;
                _cmdProcessorStates.IsRunning = false;
                await SendMessage(output, null);
                return;

            }
            if (_cmdProcessorStates.IsRunning && _cancellationTokenSource != null)
            {
                _logger.LogWarning($" Warning : Cancelling the ongoing {_cmdProcessorStates.CmdName} scan.");
                _cmdProcessorStates.RunningMessage += $"Cancelling the ongoing {_cmdProcessorStates.CmdDisplayName} scan...\n";
                if (_cancellationTokenSource != null) _cancellationTokenSource.Cancel();
            }
            else
            {
                _logger.LogInformation($"No {_cmdProcessorStates.CmdName} scan is currently running.");
                _cmdProcessorStates.CompletedMessage += $"No {_cmdProcessorStates.CmdDisplayName} scan is currently running.\n";
            }
        }


        public async Task<ResultObj> QueueCommand(CancellationTokenSource cts, ProcessorScanDataObj processorScanDataObj)
        {
            var tcs = new TaskCompletionSource<ResultObj>();
            var commandTask = new CommandTask(
                processorScanDataObj.MessageID,
                async () =>
                {
                    try
                    {
                        // Run the command and set the result in the TaskCompletionSource
                        var result = await RunCommand(processorScanDataObj.Arguments, cts.Token, processorScanDataObj);
                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex); // If an error occurs, propagate it to the caller
                    }
                },
                cts
            );
            _currentQueue.Enqueue(commandTask);
            ResultObj taskResult = await tcs.Task;
            _runningTasks.TryRemove(commandTask.MessageId, out _);

            taskResult.Message = await SendMessage(taskResult.Message, processorScanDataObj);
            return taskResult;
            // return await tcs.Task; // Return the Task<string> that will complete once the command finishes
        }

        private async Task ProcessQueueAsync()
        {
            var tasks = new List<Task>();

            while (_currentQueue.TryDequeue(out var commandTask))
            {
                try
                {
                    if (!commandTask.IsRunning)
                    {
                        await _semaphore.WaitAsync(); // Wait for a semaphore slot to be available
                        commandTask.IsRunning = true;
                        _runningTasks.TryAdd(commandTask.MessageId, commandTask);


                        // Launch a task without awaiting it directly
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                await commandTask.TaskFunc();
                                commandTask.IsSuccessful = true;
                            }
                            catch (OperationCanceledException)
                            {
                                _logger.LogInformation($"Command {commandTask.MessageId} was cancelled.");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"Command {commandTask.MessageId} failed with exception: {ex.Message}");
                            }
                            finally
                            {
                                commandTask.IsRunning = false;
                                _semaphore.Release(); // Release the semaphore slot
                            }
                        });
                        commandTask.RunningTask = task;
                        tasks.Add(task);
                    }

                }
                catch (Exception e)
                {
                    _logger.LogError($" Error : in ProcessQueue while loop : {e.Message}");
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAny(tasks); // Optionally wait for the first task to finish, or use Task.WhenAll for all
            }
            else
            {
                // If no tasks, briefly delay to prevent tight loop
                await Task.Delay(1000);
            }
        }



        public async Task<ResultObj> CancelCommand(string messageId)
        {
            var result = new ResultObj();
            try
            {
                if (_runningTasks.TryGetValue(messageId, out var taskToCancel))
                {
                    taskToCancel.CancellationTokenSource.Cancel();

                    // Wait for the running task to complete gracefully
                    if (taskToCancel.RunningTask != null)
                    {
                        try
                        {
                            await taskToCancel.RunningTask;
                        }
                        catch (OperationCanceledException)
                        {
                            // Task was canceled as expected.
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error waiting for canceled task {messageId} to finish: {ex.Message}");
                        }
                    }

                    result.Success = true;
                    result.Message = $"Task with message_id {messageId} has been cancelled and completed.";
                }
                else
                {
                    result.Success = false;
                    result.Message = $"Warning: no running command found with MessageID: {messageId}";
                    _logger.LogWarning(result.Message);
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Message = $"Error while trying to cancel message_id {messageId}. Error: {e.Message}";
                _logger.LogError(result.Message);
            }

            return result;
        }

        public virtual async Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj();
            string output = "";
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


                using (var process = new Process())
                {
                    process.StartInfo.FileName = _netConfig.CommandPath + _cmdProcessorStates.CmdName;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true; // Add this to capture standard error

                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.WorkingDirectory = _netConfig.CommandPath;

                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            errorBuilder.AppendLine(e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Register a callback to kill the process if cancellation is requested
                    using (cancellationToken.Register(() =>
                    {
                        if (!process.HasExited)
                        {
                            _logger.LogInformation($"Cancellation requested, killing the {_cmdProcessorStates.CmdDisplayName} process...");
                            process.Kill();
                        }
                    }))
                    {
                        // Wait for the process to exit or the cancellation token to be triggered
                        await process.WaitForExitAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested(); // Check if cancelled before processing output

                        output = outputBuilder.ToString();
                        string errorOutput = errorBuilder.ToString();

                        if (!string.IsNullOrWhiteSpace(errorOutput) && processorScanDataObj != null)
                        {
                            output = $"RedirectStandardError : {errorOutput}. \n RedirectStandardOutput : {output}";
                        }
                        result.Success = true;
                    }
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

        public virtual async Task<ResultObj> PublishCommandHelp(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            try
            {
                processorScanDataObj.ScanCommandOutput = GetCommandHelp();
                await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
                result.Message = $" Success : published help message with MessageID {processorScanDataObj.MessageID} output : {processorScanDataObj.ScanCommandOutput}";
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message = $" Error : could not publish help message with MessageID  {processorScanDataObj.MessageID} output : {processorScanDataObj.ScanCommandOutput} . Error was : {e.Message}";
                result.Success = false;
            }
            return result;
        }
        public virtual string GetCommandHelp()
        {
            return @"No help file available";

        }
        public virtual async Task CancelRun()
        {
            if (_cmdProcessorStates.IsCmdRunning && _cancellationTokenSource != null)
            {
                _logger.LogInformation($"Cancelling the ongoing {_cmdProcessorStates.CmdName} execution.");
                _cmdProcessorStates.RunningMessage += $"Cancelling the ongoing {_cmdProcessorStates.CmdDisplayName} execution...\n";
                _cancellationTokenSource.Cancel();
            }
            else
            {
                _logger.LogInformation($"No {_cmdProcessorStates.CmdName} execution is currently running.");
                _cmdProcessorStates.CompletedMessage += $"No {_cmdProcessorStates.CmdName} execution is currently running.\n";
            }
            await Task.CompletedTask;
        }

        protected virtual async Task<string> SendMessage(string output, ProcessorScanDataObj? processorScanDataObj)
        {
            if (processorScanDataObj == null || !processorScanDataObj.SendMessage) return output;

            try
            {
                // Set LineLimit to default if needed
                if (processorScanDataObj.LineLimit == -1)
                {
                    processorScanDataObj.LineLimit = _netConfig.CmdReturnDataLineLimit;
                }

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                // Use Html parser to split if there are 
                if (lines.Length == 1 && HtmlTextParser.IsHtml(output))
                {
                    output = HtmlTextParser.ParseHtmlContent(output);
                    lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                }
                else if (lines.Length == 0) lines = new[] { "The cmd processor gave no output" };
                int totalLines = lines.Length;
                int totalPages = (int)Math.Ceiling((double)totalLines / processorScanDataObj.LineLimit);

                // Validate Page number
                if (processorScanDataObj.Page < 1)
                {
                    processorScanDataObj.Page = 1;
                }
                else if (processorScanDataObj.Page > totalPages)
                {
                    processorScanDataObj.Page = totalPages;
                }

                // Paginate or bypass pagination based on LineLimit
                IEnumerable<string> paginatedLines;
                if (processorScanDataObj.LineLimit == -2)
                {
                    paginatedLines = lines; // All lines
                }
                else
                {
                    int startLineIndex = (processorScanDataObj.Page - 1) * processorScanDataObj.LineLimit;
                    int endLineIndex = Math.Min(startLineIndex + processorScanDataObj.LineLimit, totalLines);
                    paginatedLines = lines.Skip(startLineIndex).Take(endLineIndex - startLineIndex);
                }

                // Build output
                var sb = new StringBuilder();
                sb.AppendLine(string.Join("\n", paginatedLines));

                // Add pagination info only when needed
                if (processorScanDataObj.LineLimit != -2 && totalLines > processorScanDataObj.LineLimit)
                {
                    sb.AppendLine($"[Showing page {processorScanDataObj.Page} of {totalPages}. Total lines: {totalLines}.]");

                    if (processorScanDataObj.Page < totalPages)
                    {
                        sb.AppendLine($"[Output truncated to {processorScanDataObj.LineLimit} lines per page. Choose another page or refine the query for less data.]");
                    }
                }

                output = sb.ToString();

                // Proper JSON Serialization
                string jsonString = JsonSerializer.Serialize(output, new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                if (jsonString.StartsWith("\""))
                {
                    jsonString = jsonString.Substring(1);
                }

                // Remove trailing double quote if present
                if (jsonString.EndsWith("\""))
                {
                    jsonString = jsonString.Substring(0, jsonString.Length - 1);
                }


                processorScanDataObj.ScanCommandOutput = jsonString;
                await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
                _logger.LogInformation($"Success: Sent MessageID {processorScanDataObj.MessageID}");
            }
            catch (Exception e)
            {
                string errorMsg = $"Error during publish {_cmdProcessorStates.CmdName} command: {e.Message}";
                _logger.LogError(errorMsg);
                return $"{output}\n{errorMsg}";
            }

            return output;
        }

        protected virtual Dictionary<string, string> ParseArguments(string arguments)
        {
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var regex = new Regex(@"--(?<key>\w+)(?:\s+(?<value>""[^""]*""|\S+))?");

            var matches = regex.Matches(arguments);

            foreach (Match match in matches)
            {
                string key = match.Groups["key"].Value.ToLower();
                string value = match.Groups["value"].Success ? match.Groups["value"].Value : "true"; // Boolean flags get "true"

                // Remove quotes if the value is quoted
                if (value.StartsWith("\"") && value.EndsWith("\""))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                args[key] = value; // Overwrites duplicate keys
            }

            return args;
        }



    }
}