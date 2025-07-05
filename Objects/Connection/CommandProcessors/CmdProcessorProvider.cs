using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetworkMonitor.Objects.ServiceMessage;


namespace NetworkMonitor.Connection
{
    public interface ICmdProcessorProvider
    {
        ICmdProcessor? GetProcessor(string processorType);
        ILocalCmdProcessorStates? GetProcessorStates(string processorType);
        Task CancelCommand(string processorType, string messageId);
        Task<ResultObj> AddCmdProcessor(ProcessorScanDataObj processorScanDataObj);
        Task<ResultObj> DeleteCmdProcessor(ProcessorScanDataObj processorScanDataObj);
        List<string> ProcessorTypes { get; }
        Task<ResultObj> PublishScanProcessorDataObj(ProcessorScanDataObj processorScanDataObj);
        Task<ResultObj> PublishSourceCode(ProcessorScanDataObj processorScanDataObj);
        Task PublishAckMessage(ProcessorScanDataObj processorScanDataObj);
        Task<ResultObj> Setup();
    }

    public class CmdProcessorProvider : ICmdProcessorProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IRabbitRepo _rabbitRepo;
        private readonly NetConnectConfig _netConfig;
        private ILogger _logger;
        private readonly Dictionary<string, ILocalCmdProcessorStates> _processorStates;
        private readonly Dictionary<string, ICmdProcessor> _processors;

        private readonly List<string> _coreProcessorTypes = new()
        {
            "Nmap", "Meta", "Openssl", "Busybox", "SearchWeb", "SearchEngage", "CrawlPage", "CrawlSite", "Ping", "QuantumConnect", "QuantumPortScanner", "QuantumInfo"
        };
        private List<string> _processorTypes;
        private Dictionary<string, string> _sourceCodeFileMap = new();
        private readonly CmdProcessorCompiler _compiler;

        public List<string> ProcessorTypes { get => _processorTypes; }

        public CmdProcessorProvider(ILoggerFactory loggerFactory, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
        {
            _loggerFactory = loggerFactory;
            _rabbitRepo = rabbitRepo;
            _netConfig = netConfig;
            _processorStates = new Dictionary<string, ILocalCmdProcessorStates>();
            _processors = new Dictionary<string, ICmdProcessor>();
            _processorTypes = new List<string>(_coreProcessorTypes);
            _compiler = new CmdProcessorCompiler(_loggerFactory, _netConfig, _rabbitRepo, _processorStates, _processors, _processorTypes, _sourceCodeFileMap);

            _logger = _loggerFactory.CreateLogger<CmdProcessorProvider>();
            // Populate _sourceCodeFileMap dynamically if _netConfig.CommandPath is provided

        }


        public Task<ResultObj> Setup()
        {
            var result = new ResultObj();

            try
            {
                if (!string.IsNullOrEmpty(_netConfig.CommandPath))
                {
                    PopulateSourceCodeFileMap(_netConfig.CommandPath);
                }

                // Setup static processors
                var staticSetupResult = SetupStaticProcessors();
                if (!staticSetupResult.Success)
                {
                    result.Success = false;
                    result.Message = staticSetupResult.Message;
                    return Task.FromResult(result);
                }


                // Fire-and-forget dynamic processors setup
                Task.Run(() => SetupDynamicProcessors());

                result.Success = true;
                result.Message = "Success: Static setup complete. Dynamic setup running in the background.";
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Message = $"Error: Failed to setup CmdProcessorFactory. Error was: {e.Message}";
            }

            return Task.FromResult(result);
        }

        private ResultObj SetupStaticProcessors()
        {
            var result = new ResultObj();

            try
            {
                foreach (var processorType in ProcessorTypes)
                {
                    // Skip dynamic processors
                    if (!string.IsNullOrEmpty(_netConfig.CommandPath) && _sourceCodeFileMap.ContainsKey(processorType))
                        continue;

                    try
                    {
                        _compiler.HandleStaticProcessor(processorType);
                        _logger.LogInformation($"Success: Created static {processorType}CmdProcessor.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to initialize static processor for type '{processorType}'. Error: {ex.Message}");
                        throw; // Static setup must succeed, so rethrow
                    }
                }

                result.Success = true;
                result.Message = "Success: Static processors setup completed.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error: Failed to setup static processors. Error was: {ex.Message}";
            }

            return result;
        }

        private async Task SetupDynamicProcessors()
        {
            foreach (var processorType in ProcessorTypes)
            {
                // Skip static processors
                if (string.IsNullOrEmpty(_netConfig.CommandPath) || !_sourceCodeFileMap.ContainsKey(processorType))
                    continue;

                try
                {
                    var resultDynamic = await _compiler.HandleDynamicProcessor(processorType);

                    if (resultDynamic.Success)
                        _logger.LogInformation(resultDynamic.Message);
                    else
                        _logger.LogError(resultDynamic.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to initialize dynamic processor for type '{processorType}'. Error: {ex.Message}");
                }
            }
        }


        public async Task CancelCommand(string processorType, string messageId)
        {
            //processorType = processorType.ToLower();
            if (_processors.TryGetValue(processorType, out var processor))
            {
                await processor.CancelCommand(messageId);
            }
            else
            {
                throw new ArgumentException($"Processor type '{processorType}' not found.");
            }
        }
       
        public ICmdProcessor? GetProcessor(string processorType)
        {
            return _processors.FirstOrDefault(kvp =>
                string.Equals(kvp.Key, processorType, StringComparison.OrdinalIgnoreCase)).Value;
        }


        public ILocalCmdProcessorStates? GetProcessorStates(string processorType)
        {
            // processorType = processorType.ToLower();
            return _processorStates.TryGetValue(processorType, out var states) ? states : null;
        }


        public async Task<ResultObj> AddCmdProcessor(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            // Make sure type does not contain CmdProcessor.
            if (processorScanDataObj.Type.Contains(" "))
            {
                string helperName = processorScanDataObj.Type.Replace(" ", "");
                result.Success = false;
                result.Message = $" Error : cmd_processor_type '{processorScanDataObj.Type}' must not contain spaces. Try using cmd_processor_type {helperName} and use the class Name {helperName}CmdProcessor";

            }
            else if (processorScanDataObj.Type.Contains("CmdProcessor"))
            {
                string helperName = processorScanDataObj.Type.Replace("CmdProcessor", "");
                result.Success = false;
                result.Message = $" Error : cmd_processor_type '{processorScanDataObj.Type}' contains the word CmdProcessor . Try using cmd_processor_type {helperName} and use the class Name {helperName}CmdProcessor";

            }
            // Make sure class name is Type plus CmdProcessor
            else if (!processorScanDataObj.Arguments.Contains($"public class {processorScanDataObj.Type}CmdProcessor"))
            {
                result.Success = false;
                result.Message = $" Error : For the cmd_processor_type {processorScanDataObj.Type} you must use public class {processorScanDataObj.Type}CmdProcessor . Please correct the class name and try again. ";

            }
            else if (!processorScanDataObj.Arguments.Contains("namespace NetworkMonitor.Connection"))
            {
                result.Success = false;
                result.Message = $" Error : you must included the namespace NetworkMonitor.Connection . Please enslose the class definition in a namespace decleration : namespace NetworkMonitor.Connection {{ public class {processorScanDataObj.Type}CmdProcessor .... }}  ";
            }
            else
            {
                _logger.LogInformation($"\n\n Attempting to compile source code :\n\n{processorScanDataObj.Arguments}\n\n");
                result = await _compiler.HandleDynamicProcessor(processorScanDataObj.Type, argsEscaped: processorScanDataObj.ArgsEscaped, processorSourceCode: processorScanDataObj.Arguments);
            }


            processorScanDataObj.ScanCommandOutput = result.Message;
            await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
            return result;
        }


        public async Task<ResultObj> DeleteCmdProcessor(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            if (_coreProcessorTypes.Contains(processorScanDataObj.Type))
            {
                result.Message = $" Error : cannot delete cmd processor of type {processorScanDataObj.Type} as it is a core cmd processor built into the agent and cannot be deleted.";
                result.Success = false;
            }
            else
            {
                try
                {
                    // Remove from processor states and processors dictionaries if present
                    _processorStates.Remove(processorScanDataObj.Type);
                    _processors.Remove(processorScanDataObj.Type);

                    // Remove from _processorTypes if present
                    _processorTypes.Remove(processorScanDataObj.Type);

                    // Attempt to remove source files
                    // If a dynamic processor was added, it may have an entry in _sourceCodeFileMap
                    if (_sourceCodeFileMap.TryGetValue(processorScanDataObj.Type, out var sourceFilePath))
                    {
                        if (File.Exists(sourceFilePath))
                        {
                            File.Delete(sourceFilePath);
                        }
                        _sourceCodeFileMap.Remove(processorScanDataObj.Type);
                    }

                    // Also try to delete the dynamically saved CmdProcessor.cs file, if it exists in _netConfig.CommandPath
                    if (!string.IsNullOrEmpty(_netConfig.CommandPath))
                    {
                        var savePath = Path.Combine(_netConfig.CommandPath, $"{processorScanDataObj.Type}CmdProcessor.cs");
                        if (File.Exists(savePath))
                        {
                            File.Delete(savePath);
                        }
                    }

                    result.Message = $" Success : deleted cmd processor of type {processorScanDataObj.Type}.";
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.Message = $" Error : unable to delete cmd processor of type {processorScanDataObj.Type}. Error: {ex.Message}";
                    result.Success = false;
                }
            }


            processorScanDataObj.ScanCommandOutput = result.Message;
            await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
            return result;
        }

        public async Task PublishAckMessage(ProcessorScanDataObj processorScanDataObj)
        {
            // Send acknowledgment to RabbitMQ
            try
            {
                // Prepare the acknowledgment message
                processorScanDataObj.ScanCommandOutput = $"Acknowledged command with MessageID {processorScanDataObj.MessageID}";
                processorScanDataObj.IsAck = true;
                // Publish the acknowledgment to RabbitMQ
                await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService + "Ack", processorScanDataObj);

                _logger.LogInformation($"Acknowledgment sent for MessageID {processorScanDataObj.MessageID}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending acknowledgment for MessageID {processorScanDataObj.MessageID}: {ex.Message}");
            }
        }

        public async Task<ResultObj> PublishScanProcessorDataObj(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            try
            {
                await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
                result.Message = $" Success : published  message with MessageID {processorScanDataObj.MessageID} output : {processorScanDataObj.ScanCommandOutput}";
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message = $" Error : could not publish message with MessageID  {processorScanDataObj.MessageID} output : {processorScanDataObj.ScanCommandOutput} . Error was : {e.Message}";
                result.Success = false;
            }
            return result;
        }

        public async Task<ResultObj> PublishSourceCode(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            try
            {
                processorScanDataObj.ScanCommandOutput = await GetSourceCode(processorScanDataObj.Type);
                await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
                result.Message = $" Success : published  soruce code message with MessageID {processorScanDataObj.MessageID} output : {processorScanDataObj.ScanCommandOutput}";
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message = $" Error : could not publish source code message with MessageID  {processorScanDataObj.MessageID} output : {processorScanDataObj.ScanCommandOutput} . Error was : {e.Message}";
                result.Success = false;
            }
            return result;
        }

        private async Task<string> GetSourceCode(string processorType)
        {
            if (_sourceCodeFileMap.TryGetValue(processorType, out var sourceFilePath))
            {
                try
                {
                    return await File.ReadAllTextAsync(sourceFilePath);
                }
                catch (Exception ex)
                {
                    return $"Error: Unable to load source code for processor type '{processorType}'. Details: {ex.Message}";
                }
            }
            else if (_coreProcessorTypes.Contains(processorType))
            {
                return $"The source code for core cmd processors like '{processorType}' is not available. However you can get help for the cmd procesor by calling get_cmd_processor_help using {processorType} for cmd_processor_type";
            }
            else
            {
                return $"No source code found for processor type '{processorType}'.";
            }
        }

        private void PopulateSourceCodeFileMap(string directory)
        {
            try
            {
                // Get all files matching *CmdProcessor.cs in the directory
                var cmdProcessorFiles = System.IO.Directory.GetFiles(directory, "*CmdProcessor.cs");

                foreach (var filePath in cmdProcessorFiles)
                {
                    // Extract the processor type from the file name (e.g., SimpleCmdProcessor.cs -> Simple)
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    if (fileName.EndsWith("CmdProcessor"))
                    {
                        var processorType = fileName.Replace("CmdProcessor", "");
                        if (!_sourceCodeFileMap.ContainsKey(processorType))
                        {
                            _sourceCodeFileMap[processorType] = filePath;
                            ProcessorTypes.Add(processorType); // Add to the processor types list

                            _logger.LogInformation($"Added dynamic processor '{processorType}' from file '{filePath}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to populate source code file map from directory '{directory}'. Error: {ex.Message}");
            }
        }

    }
}
