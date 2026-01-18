using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Connection
{
    public sealed class QuantumCertificateProbe
    {
        private readonly string _oqsProviderPath;
        private readonly string _commandPath;
        private readonly string _nativeLibDir;
        private readonly ILogger _logger;
        private readonly IPlatformProcessRunner _runner;

        public QuantumCertificateProbe(NetConnectConfig netConfig, ILogger logger)
        {
            _oqsProviderPath = netConfig.OqsProviderPath;
            _commandPath = netConfig.CommandPath;
            _nativeLibDir = netConfig.NativeLibDir;
            _logger = logger;

#if ANDROID
            _runner = new AndroidProcWrapperRunner(logger);
#else
            _runner = new DefaultProcessRunner(logger);
#endif
        }

        public QuantumCertificateProbe(string oqsProviderPath, string commandPath, string nativeLibDir, ILogger logger)
        {
            _oqsProviderPath = oqsProviderPath;
            _commandPath = commandPath;
            _nativeLibDir = nativeLibDir;
            _logger = logger;

#if ANDROID
            _runner = new AndroidProcWrapperRunner(logger);
#else
            _runner = new DefaultProcessRunner(logger);
#endif
        }

        public async Task<ResultObj> CheckAsync(string address, int port, CancellationToken token)
        {
            var output = await RunOpenSslAsync(address, port, token);

            if (QuantumCertificateAnalyzer.TryBuildSummary(output, out var summary))
            {
                return new ResultObj
                {
                    Success = summary.IsQuantumSafeCertificate,
                    Message = summary.ToSummaryString(),
                    Data = summary
                };
            }

            return new ResultObj
            {
                Success = false,
                Message = "Certificate summary not found in handshake output."
            };
        }

        private async Task<string> RunOpenSslAsync(string address, int port, CancellationToken token)
        {
            string providerName = "oqsprovider";
            string workingDirectory = string.IsNullOrEmpty(_nativeLibDir) ? _commandPath : _nativeLibDir;
            string providerPath = string.IsNullOrEmpty(_nativeLibDir) ? _oqsProviderPath : _nativeLibDir;
            string opensslPath = System.IO.Path.Combine(
                workingDirectory,
                "openssl" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : ""));

            if (!string.IsNullOrEmpty(_nativeLibDir))
            {
                opensslPath = System.IO.Path.Combine(workingDirectory, "libopenssl_exec.so");
                providerName = "liboqsprovider";
            }

            var args = $"s_client -connect {address}:{port} -showcerts " +
                       $"-provider-path {providerPath} -provider {providerName} -provider default";

            var envVars = new Dictionary<string, string>
            {
                ["LD_LIBRARY_PATH"] = workingDirectory
            };

            if (ShouldIncludeSni(address))
            {
                args += $" -servername {address}";
            }

            _logger.LogDebug("Preparing to run openssl: {Cmd} {Args}", opensslPath, args);
            return await _runner.RunAsync(opensslPath, args, workingDirectory, envVars, token);
        }

        private static bool ShouldIncludeSni(string address)
        {
            return !IPAddress.TryParse(address, out _);
        }
    }
}
