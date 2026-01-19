using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Connection
{
    public class QuantumCertConnect : NetConnect
    {
        private readonly QuantumCertificateProbe _probe;
        private readonly ILogger _logger;

        public QuantumCertConnect(string oqsProviderPath, string commandPath, string nativeLibDir, ILogger logger, List<AlgorithmInfo> algorithms)
        {
            _probe = new QuantumCertificateProbe(oqsProviderPath, commandPath, nativeLibDir, logger, algorithms);
            _logger = logger;
            IsLongRunning = true;
        }

        public override async Task Connect()
        {
            PreConnect();
            int port = MpiStatic.Port != 0 ? MpiStatic.Port : 443;

            try
            {
                Timer.Reset();
                Timer.Start();
                var result = await _probe.CheckAsync(MpiStatic.Address, port, Cts.Token);
                Timer.Stop();

                if (result.Success)
                {
                    ProcessStatus("Quantum-safe certificate detected",
                                  (ushort)Timer.ElapsedMilliseconds,
                                  " : " + result.Message);
                }
                else
                {
                    ProcessException(result.Message ?? "Certificate not quantum-safe", "Certificate not quantum-safe");
                }
            }
            catch (OperationCanceledException)
            {
                ProcessException("Timeout", "Timeout");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quantum certificate check failed.");
                ProcessException(ex.Message, "Exception");
            }
            finally
            {
                PostConnect();
            }
        }
    }
}
