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
        private readonly List<AlgorithmInfo> _algorithms;

        public QuantumCertificateProbe(NetConnectConfig netConfig, ILogger logger)
        {
            _oqsProviderPath = netConfig.OqsProviderPath;
            _commandPath = netConfig.CommandPath;
            _nativeLibDir = netConfig.NativeLibDir;
            _logger = logger;
            _algorithms = ConnectHelper.GetAlgorithmInfoList(netConfig);

#if ANDROID
            _runner = new AndroidProcWrapperRunner(logger);
#else
            _runner = new DefaultProcessRunner(logger);
#endif
        }

        public QuantumCertificateProbe(string oqsProviderPath, string commandPath, string nativeLibDir, ILogger logger, List<AlgorithmInfo> algorithms)
        {
            _oqsProviderPath = oqsProviderPath;
            _commandPath = commandPath;
            _nativeLibDir = nativeLibDir;
            _logger = logger;
            _algorithms = algorithms ?? new List<AlgorithmInfo>();

#if ANDROID
            _runner = new AndroidProcWrapperRunner(logger);
#else
            _runner = new DefaultProcessRunner(logger);
#endif
        }

        public async Task<ResultObj> CheckAsync(string address, int port, CancellationToken token)
        {
            var enabled = _algorithms.Where(a => a.Enabled).ToList();
            var modern = enabled.Where(a => !a.AddEnv).ToList();
            var legacy = enabled.Where(a => a.AddEnv).ToList();

            if (modern.Any())
            {
                var groups = string.Join(":", modern.Select(a => a.AlgorithmName));
                var output = await RunOpenSslAsync(address, port, groups, null, token);
                if (TryBuildResult(output, out var result))
                    return result;
            }

            foreach (var algo in legacy)
            {
                if (string.IsNullOrWhiteSpace(algo.EnvironmentVariable)) continue;
                var env = new Dictionary<string, string>
                {
                    [algo.EnvironmentVariable] = algo.DefaultID.ToString()
                };
                var output = await RunOpenSslAsync(address, port, algo.AlgorithmName, env, token);
                if (TryBuildResult(output, out var result))
                    return result;
            }

            // Fallback: attempt without groups to support classical endpoints.
            {
                var output = await RunOpenSslAsync(address, port, string.Empty, null, token);
                if (TryBuildResult(output, out var result))
                    return result;
            }

            return new ResultObj
            {
                Success = false,
                Message = "Certificate summary not found in handshake output."
            };
        }

        private async Task<string> RunOpenSslAsync(
            string address,
            int port,
            string groups,
            Dictionary<string, string>? extraEnv,
            CancellationToken token)
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

            var args = $"s_client -connect {address}:{port} -showcerts ";
            if (!string.IsNullOrWhiteSpace(groups))
            {
                args += $"-groups {groups} ";
            }
            args +=
                       $"-provider-path {providerPath} -provider {providerName} -provider default";

            var envVars = new Dictionary<string, string>
            {
                ["LD_LIBRARY_PATH"] = workingDirectory,
                ["NM_CLOSE_STDIN"] = "true"
            };
            if (extraEnv != null)
            {
                foreach (var kv in extraEnv)
                {
                    envVars[kv.Key] = kv.Value;
                }
            }

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

        private static bool TryBuildResult(string output, out ResultObj result)
        {
            if (QuantumCertificateAnalyzer.TryBuildSummary(output, out var summary))
            {
                result = new ResultObj
                {
                    Success = summary.IsQuantumSafeCertificate,
                    Message = summary.ToSummaryString(),
                    Data = summary
                };
                return true;
            }

            result = new ResultObj
            {
                Success = false,
                Message = "Certificate summary not found in handshake output."
            };
            return false;
        }
    }
}
