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

                var config = new QuantumTestConfig(
                    Target: target,
                    Port: parsedArgs.GetInt("port", 443),
                    Algorithms: parsedArgs.GetList("algorithms", GetDefaultAlgorithms()),
                    Timeout: parsedArgs.GetInt("timeout", _defaultTimeout)
                );

                return await ExecuteQuantumTest(config, cancellationToken);
            }
            catch (Exception ex)
            {
                return new ResultObj { Message = $"Quantum check failed: {ex.Message}" };
            }
        }

      

      

        
    }
}