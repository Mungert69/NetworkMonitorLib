using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Connection
{
    public class QuantumInfoCmdProcessor : CmdProcessor
    {
        private readonly Dictionary<string, AlgorithmInfo> _algorithmInfoMap;

        public QuantumInfoCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            // Load algorithm information from the JSON file
            var jsonFilePath = Path.Combine(netConfig.OqsProviderPath, "algo_info.json");
            _algorithmInfoMap = ConnectHelper.GetAlgorithmInfoFromJson(jsonFilePath);
        }

   public override  Task<ResultObj> RunCommand(
    string arguments,
    CancellationToken cancellationToken,
    ProcessorScanDataObj? processorScanDataObj = null)
{
    try
    {
        var parsedArgs = base.ParseArguments(arguments);
        var algorithmName = parsedArgs.GetString("algorithm","");

        if (string.IsNullOrEmpty(algorithmName))
        {
            return Task.FromResult(new ResultObj { Message = "Algorithm name is required." });
        }

        // Try to find an exact match first
        if (_algorithmInfoMap.TryGetValue(algorithmName, out var algorithmInfo))
        {
            var message = $@"
Algorithm: {algorithmInfo.AlgorithmName}
Description: {algorithmInfo.Description}
Key Size: {algorithmInfo.KeySize} bits
Security Level: {algorithmInfo.SecurityLevel}
";

              return Task.FromResult(new ResultObj
            {
                Message = message,
                Success = true,
                Data = algorithmInfo
            });
        }

        // If no exact match, find all algorithms that contain the search term (case-insensitive)
        var matchingAlgorithms = _algorithmInfoMap
            .Where(kvp => kvp.Key.Contains(algorithmName, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Value)
            .ToList();

        if (matchingAlgorithms.Count == 0)
        {
            return Task.FromResult(new ResultObj { Message = $"No algorithms found matching '{algorithmName}'." });
        }

        if (matchingAlgorithms.Count == 1)
        {
            // If only one match, return its details
            algorithmInfo = matchingAlgorithms[0];
            var message = $@"
Algorithm: {algorithmInfo.AlgorithmName}
Description: {algorithmInfo.Description}
Key Size: {algorithmInfo.KeySize} bits
Security Level: {algorithmInfo.SecurityLevel}
";

              return Task.FromResult(new ResultObj
            {
                Message = message,
                Success = true,
                Data = algorithmInfo
            });
        }

        // If multiple matches, return a list of matching algorithms
        var algorithmList = string.Join("\n", matchingAlgorithms.Select(a => $"- {a.AlgorithmName}"));
        var listMessage = $@"
Multiple algorithms found matching '{algorithmName}'. Please specify one of the following:
{algorithmList}
";

       return Task.FromResult(new ResultObj
        {
            Message = listMessage,
            Success = false
        });
    }
    catch (Exception ex)
    {
       return Task.FromResult(new ResultObj { Message = $"Failed to retrieve algorithm info: {ex.Message}" });
    }
}
     public override string GetCommandHelp()
{
    var supportedAlgorithms = string.Join("\n  - ", _algorithmInfoMap.Keys);

    return $@"
Quantum Algorithm Info Help
===========================
Retrieves static information about a specified quantum-safe algorithm.

Usage:
  --algorithm <algorithm_name>

Supported Algorithms:
  - {supportedAlgorithms}

Search Options:
  - You can search for algorithms using partial names (case-insensitive).
  - If multiple algorithms match your search, a list of matches will be displayed.

Examples:
  --algorithm Kyber512          # Retrieves details for the exact algorithm 'Kyber512'.
  --algorithm Kyber             # Searches for algorithms containing 'Kyber' (e.g., Kyber512, Kyber768).
  --algorithm ML                # Searches for algorithms containing 'ML' (e.g., ML-KEM-512, ML-KEM-768).

Note:
  If no exact match is found, the system will return a list of algorithms that partially match your search term.
";
}
    }
}