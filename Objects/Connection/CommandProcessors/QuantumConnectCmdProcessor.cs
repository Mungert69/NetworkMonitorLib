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
    public class QuantumConnectCmdProcessor : QuantumTestBase
    {
        public QuantumConnectCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig, 
                  "quantum", "Quantum Security Check")
        {
        }

        public override async Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                    return new ResultObj { Message = "Quantum security checks not available" };

                var parsedArgs = base.ParseArguments(arguments);
                var target = GetPositionalArgument(arguments);
                target = target.Replace("https://", "");
                target = target.Replace("http://", "");
                if (string.IsNullOrEmpty(target))
                    return new ResultObj { Message = "Missing required target parameter" };

                // Do NOT pass a default here
                var userAlgos = parsedArgs.GetList("algorithms", new());
                List<string> algosToUse;
                bool batch;
                if (userAlgos == null || userAlgos.Count == 0)
                {
                    algosToUse = GetDefaultAlgorithms();
                    batch = true;
                }
                else
                {
                    algosToUse = userAlgos;
                    batch = false;
                }

                var config = new QuantumTestConfig(
                    Target: target,
                    Port: parsedArgs.GetInt("port", 443),
                    Algorithms: algosToUse,
                    Timeout: parsedArgs.GetInt("timeout", _defaultTimeout)
                );

                List<AlgorithmResult> results;
                if (batch)
                {
                    // Batch mode: test all at once
                    results = await ProcessAlgorithmGroup(config, algosToUse, cancellationToken);
                }
                else
                {
                    // One-by-one: test each separately
                    results = await ProcessAlgorithmGroupOneByOne(config, algosToUse, cancellationToken);
                }

                return ProcessTestResults(results);
            }
            catch (Exception ex)
            {
                return new ResultObj { Message = $"Quantum check failed: {ex.Message}" };
            }
        }
    }
}
