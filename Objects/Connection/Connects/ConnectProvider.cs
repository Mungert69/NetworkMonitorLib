using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Connection
{
    public interface IConnectProvider
    {
        INetConnect? CreateConnect(string connectType);
        Task<ResultObj> AddConnect(ProcessorScanDataObj processorScanDataObj);
        Task<ResultObj> DeleteConnect(ProcessorScanDataObj processorScanDataObj);
        List<string> ConnectTypes { get; }
        Task<ResultObj> PublishSourceCode(ProcessorScanDataObj processorScanDataObj);
        Task<ResultObj> PublishConnectList(ProcessorScanDataObj processorScanDataObj);
        Task PublishAckMessage(ProcessorScanDataObj processorScanDataObj);
        Task<ResultObj> Setup();
    }

    public class ConnectProvider : IConnectProvider
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly IRabbitRepo _rabbitRepo;
        private readonly NetConnectConfig _netConfig;
        private readonly IBrowserHost? _browserHost;
        private readonly ICmdProcessorProvider? _cmdProcessorProvider;
        private readonly ConnectCompiler _compiler;

        private readonly Dictionary<string, Type> _dynamicConnectTypes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _sourceCodeFileMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _connectTypes;

        private readonly List<string> _coreConnectTypes = new()
        {
            "icmp", "http", "https", "httphtml", "httpfull", "sitehash", "dns", "smtp", "quantum",
            "quantumcert", "rawconnect", "blebroadcast", "blebroadcastlisten", "nmap", "nmapvuln",
            "crawlsite", "dailycrawl", "dailyhugkeepalive", "hugwake"
        };

        public List<string> ConnectTypes => new(_connectTypes);

        public ConnectProvider(
            ILoggerFactory loggerFactory,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig,
            IBrowserHost? browserHost = null,
            ICmdProcessorProvider? cmdProcessorProvider = null)
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<ConnectProvider>();
            _rabbitRepo = rabbitRepo;
            _netConfig = netConfig;
            _browserHost = browserHost;
            _cmdProcessorProvider = cmdProcessorProvider;
            _connectTypes = new List<string>(_coreConnectTypes);

            _compiler = new ConnectCompiler(
                _loggerFactory,
                _netConfig,
                _rabbitRepo,
                _browserHost,
                _cmdProcessorProvider);
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

                Task.Run(() => SetupDynamicConnects());

                result.Success = true;
                result.Message = "Success: Connect provider setup complete. Dynamic connects loading in background.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error: Failed to setup connect provider. Error was: {ex.Message}";
            }

            return Task.FromResult(result);
        }

        public INetConnect? CreateConnect(string connectType)
        {
            if (_dynamicConnectTypes.TryGetValue(connectType, out var type))
            {
                return _compiler.CreateConnectInstance(type);
            }

            return null;
        }

        public async Task<ResultObj> AddConnect(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            var connectType = processorScanDataObj.Type?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(connectType))
            {
                result.Success = false;
                result.Message = "Error : connect_type was not provided.";
            }
            else if (connectType.Contains(' '))
            {
                result.Success = false;
                result.Message = $"Error : connect_type '{connectType}' must not contain spaces.";
            }
            else if (connectType.Contains("Connect", StringComparison.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = $"Error : connect_type '{connectType}' must not contain the word Connect. Use class name {connectType}Connect.";
            }
            else if (_coreConnectTypes.Contains(connectType, StringComparer.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = $"Error : cannot add connect type '{connectType}' because it is a core connect.";
            }
            else if (!processorScanDataObj.Arguments.Contains($"public class {connectType}Connect", StringComparison.Ordinal))
            {
                result.Success = false;
                result.Message = $"Error : For the connect_type {connectType} you must use public class {connectType}Connect .";
            }
            else if (!processorScanDataObj.Arguments.Contains("namespace NetworkMonitor.Connection", StringComparison.Ordinal))
            {
                result.Success = false;
                result.Message = "Error : you must include namespace NetworkMonitor.Connection in your source code.";
            }
            else
            {
                try
                {
                    var compileResult = await CompileAndRegisterConnect(connectType, processorScanDataObj.Arguments);
                    result.Success = compileResult.Success;
                    result.Message = compileResult.Message;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error : failed to add connect type {connectType}. Error was : {ex.Message}";
                }
            }

            processorScanDataObj.ScanCommandOutput = result.Message;
            processorScanDataObj.ScanCommandSuccess = result.Success;
            await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
            return result;
        }

        public async Task<ResultObj> DeleteConnect(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            var connectType = processorScanDataObj.Type?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(connectType))
            {
                result.Success = false;
                result.Message = "Error : connect_type was not provided.";
            }
            else if (_coreConnectTypes.Contains(connectType, StringComparer.OrdinalIgnoreCase))
            {
                result.Success = false;
                result.Message = $"Error : cannot delete connect type {connectType} because it is a core connect.";
            }
            else
            {
                try
                {
                    _dynamicConnectTypes.Remove(connectType);
                    _connectTypes.RemoveAll(t => string.Equals(t, connectType, StringComparison.OrdinalIgnoreCase));

                    if (_sourceCodeFileMap.TryGetValue(connectType, out var sourceFilePath))
                    {
                        if (File.Exists(sourceFilePath))
                        {
                            File.Delete(sourceFilePath);
                        }
                        _sourceCodeFileMap.Remove(connectType);
                    }

                    if (!string.IsNullOrEmpty(_netConfig.CommandPath))
                    {
                        var savePath = Path.Combine(_netConfig.CommandPath, $"{connectType}Connect.cs");
                        if (File.Exists(savePath))
                        {
                            File.Delete(savePath);
                        }
                    }

                    result.Success = true;
                    result.Message = $"Success : deleted connect type {connectType}.";
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Error : unable to delete connect type {connectType}. Error: {ex.Message}";
                }
            }

            processorScanDataObj.ScanCommandOutput = result.Message;
            processorScanDataObj.ScanCommandSuccess = result.Success;
            await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
            return result;
        }

        public async Task<ResultObj> PublishSourceCode(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            try
            {
                processorScanDataObj.ScanCommandOutput = await GetSourceCode(processorScanDataObj.Type);
                processorScanDataObj.ScanCommandSuccess = true;
                await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
                result.Success = true;
                result.Message = $"Success : published source code for connect type {processorScanDataObj.Type}.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error : could not publish source code. Error was : {ex.Message}";
            }

            return result;
        }

        public async Task<ResultObj> PublishConnectList(ProcessorScanDataObj processorScanDataObj)
        {
            var result = new ResultObj();
            try
            {
                var connectTypesString = string.Join(", ", _connectTypes.Select(type => $"'{type}'"));
                processorScanDataObj.ScanCommandOutput = $"Success: got the list of connect types for the agent. connect_types : [{connectTypesString}]";
                processorScanDataObj.ScanCommandSuccess = true;
                await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService, processorScanDataObj);
                result.Success = true;
                result.Message = processorScanDataObj.ScanCommandOutput;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error : unable to publish connect list. Error was : {ex.Message}";
            }

            return result;
        }

        public async Task PublishAckMessage(ProcessorScanDataObj processorScanDataObj)
        {
            try
            {
                processorScanDataObj.ScanCommandOutput = $"Acknowledged command with MessageID {processorScanDataObj.MessageID}";
                processorScanDataObj.IsAck = true;
                processorScanDataObj.ScanCommandSuccess = true;
                await _rabbitRepo.PublishAsync<ProcessorScanDataObj>(processorScanDataObj.CallingService + "Ack", processorScanDataObj);
                _logger.LogInformation($"Acknowledgment sent for MessageID {processorScanDataObj.MessageID}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending acknowledgment for MessageID {processorScanDataObj.MessageID}: {ex.Message}");
            }
        }

        private async Task<ResultObj> CompileAndRegisterConnect(string connectType, string? sourceCode = null)
        {
            var result = new ResultObj();
            if (string.IsNullOrEmpty(sourceCode))
            {
                if (!_sourceCodeFileMap.TryGetValue(connectType, out var sourceFile))
                {
                    result.Success = false;
                    result.Message = $"Error : source code not found for connect type {connectType}.";
                    return result;
                }

                sourceCode = await File.ReadAllTextAsync(sourceFile);
            }

            var typeName = $"NetworkMonitor.Connection.{connectType}Connect";
            var type = _compiler.CompileAndGetType(sourceCode, typeName);

            _dynamicConnectTypes[connectType] = type;
            if (!_connectTypes.Contains(connectType, StringComparer.OrdinalIgnoreCase))
            {
                _connectTypes.Add(connectType);
            }

            if (!string.IsNullOrEmpty(_netConfig.CommandPath))
            {
                var savePath = Path.Combine(_netConfig.CommandPath, $"{connectType}Connect.cs");
                Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                await File.WriteAllTextAsync(savePath, sourceCode);
                _sourceCodeFileMap[connectType] = savePath;
            }

            result.Success = true;
            result.Message = $"Success : compiled and registered connect type {connectType}.";
            return result;
        }

        private async Task SetupDynamicConnects()
        {
            foreach (var connectType in _sourceCodeFileMap.Keys)
            {
                if (_coreConnectTypes.Contains(connectType, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var result = await CompileAndRegisterConnect(connectType);
                    if (result.Success)
                    {
                        _logger.LogInformation(result.Message);
                    }
                    else
                    {
                        _logger.LogError(result.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to initialize dynamic connect for type '{connectType}'.");
                }
            }
        }

        private async Task<string> GetSourceCode(string connectType)
        {
            if (_sourceCodeFileMap.TryGetValue(connectType, out var sourceFilePath))
            {
                try
                {
                    return await File.ReadAllTextAsync(sourceFilePath);
                }
                catch (Exception ex)
                {
                    return $"Error: Unable to load source code for connect type '{connectType}'. Details: {ex.Message}";
                }
            }
            else if (_coreConnectTypes.Contains(connectType, StringComparer.OrdinalIgnoreCase))
            {
                return $"The source code for core connects like '{connectType}' is not available.";
            }
            else
            {
                return $"No source code found for connect type '{connectType}'.";
            }
        }

        private void PopulateSourceCodeFileMap(string directory)
        {
            try
            {
                var connectFiles = Directory.GetFiles(directory, "*Connect.cs");
                foreach (var filePath in connectFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    if (!fileName.EndsWith("Connect", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var connectType = fileName[..^"Connect".Length];
                    if (string.IsNullOrWhiteSpace(connectType))
                    {
                        continue;
                    }

                    if (_coreConnectTypes.Contains(connectType, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _sourceCodeFileMap[connectType] = filePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to populate connect source code map: {Message}", ex.Message);
            }
        }
    }
}
