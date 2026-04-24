using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Connection
{
    public class ConnectHelper
    {
        /// <summary>
        /// Loads algorithm information from a JSON file and returns it as a dictionary.
        /// </summary>
        /// <param name="jsonFilePath">The path to the JSON file containing algorithm information.</param>
        /// <returns>A dictionary mapping algorithm names to their details.</returns>
        public static Dictionary<string, AlgorithmInfo> GetAlgorithmInfoFromJson(string jsonFilePath)
        {
            var json = File.ReadAllText(jsonFilePath);
            var algorithms = JsonSerializer.Deserialize<Dictionary<string, List<AlgorithmInfo>>>(json);
            var algorithmInfoMap = new Dictionary<string, AlgorithmInfo>();
            if (algorithms == null) return algorithmInfoMap;
            foreach (var algorithm in algorithms["algorithms"])
            {
                algorithmInfoMap[algorithm.AlgorithmName] = algorithm;
            }

            return algorithmInfoMap;
        }

        /// <summary>
        /// Loads algorithm information from a CSV file and enables algorithms based on runtime supported groups,
        /// with fallback to the curves file.
        /// </summary>
        /// <param name="netConfig">The network configuration containing the paths to provider assets and command binaries.</param>
        /// <returns>A list of AlgorithmInfo objects with enabled/disabled status.</returns>
        public static List<AlgorithmInfo> GetAlgorithmInfoList(NetConnectConfig netConfig, ILogger? logger = null)
        {
            // Load algorithm information from the CSV file
            string csvFilePath = Path.Combine(netConfig.OqsProviderPath, "AlgoTable.csv");
            var algorithmInfoList = CsvParser.ParseAlgorithmInfoCsv(csvFilePath);

            // Prefer runtime capability discovery; fall back to the static curves file for compatibility.
            var groups = ResolveSupportedGroups(netConfig, logger ?? NullLogger.Instance);

            // Enable algorithms that are in the curves file
            foreach (AlgorithmInfo algoInfo in algorithmInfoList)
            {
                algoInfo.Enabled = groups.Contains(algoInfo.AlgorithmName);
            }

            return algorithmInfoList;
        }

        private static HashSet<string> ResolveSupportedGroups(NetConnectConfig netConfig, ILogger logger)
        {
            var runtimeGroups = TryGetRuntimeSupportedGroups(netConfig, logger);
            if (runtimeGroups.Count > 0)
            {
                return runtimeGroups;
            }

            return LoadGroupsFromCurvesFile(netConfig);
        }

        private static HashSet<string> LoadGroupsFromCurvesFile(NetConnectConfig netConfig)
        {
            var curvesPath = Path.Combine(netConfig.OqsProviderPath, "curves");
            if (!File.Exists(curvesPath))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var content = string.Join('\n', File.ReadAllLines(curvesPath));
            return ParseGroupTokens(content);
        }

        private static HashSet<string> TryGetRuntimeSupportedGroups(NetConnectConfig netConfig, ILogger logger)
        {
            try
            {
                string workingDirectory = string.IsNullOrEmpty(netConfig.NativeLibDir)
                    ? netConfig.CommandPath
                    : netConfig.NativeLibDir;
                string providerPath = string.IsNullOrEmpty(netConfig.NativeLibDir)
                    ? netConfig.OqsProviderPath
                    : netConfig.NativeLibDir;
                string providerName = string.IsNullOrEmpty(netConfig.NativeLibDir)
                    ? "oqsprovider"
                    : "liboqsprovider";
                string opensslPath = string.IsNullOrEmpty(netConfig.NativeLibDir)
                    ? Path.Combine(workingDirectory, "openssl" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : ""))
                    : Path.Combine(workingDirectory, "libopenssl_exec.so");

                if (string.IsNullOrWhiteSpace(opensslPath) || !File.Exists(opensslPath))
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var runner = CreateRunner(logger);
                var allGroups = RunOpenSslList(runner, opensslPath, workingDirectory, providerPath, providerName, "-all-tls-groups");
                if (allGroups.Count > 0)
                {
                    return allGroups;
                }

                return RunOpenSslList(runner, opensslPath, workingDirectory, providerPath, providerName, "-tls-groups");
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static IPlatformProcessRunner CreateRunner(ILogger logger)
        {
#if ANDROID
            return new AndroidProcWrapperRunner(logger);
#else
            return new DefaultProcessRunner(logger);
#endif
        }

        private static HashSet<string> RunOpenSslList(
            IPlatformProcessRunner runner,
            string opensslPath,
            string workingDirectory,
            string providerPath,
            string providerName,
            string listFlag)
        {
            var envVars = new Dictionary<string, string>
            {
                ["NM_CLOSE_STDIN"] = "true",
                ["OPENSSL_MODULES"] = providerPath
            };

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                envVars["LD_LIBRARY_PATH"] = $"{providerPath}:{workingDirectory}";
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            string output;
            try
            {
                output = runner
                    .RunAsync(
                        opensslPath,
                        $"list {listFlag} -provider-path \"{providerPath}\" -provider {providerName} -provider default",
                        workingDirectory,
                        envVars,
                        cts.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return ParseGroupTokens(output);
        }

        private static HashSet<string> ParseGroupTokens(string rawText)
        {
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return groups;
            }

            var tokens = rawText
                .Split(new[] { ':', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t));

            foreach (var token in tokens)
            {
                if (token.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Regex.IsMatch(token, @"^[A-Za-z0-9][A-Za-z0-9._-]*$"))
                {
                    continue;
                }

                groups.Add(token);
            }

            return groups;
        }

        /// <summary>
        /// Loads quantum-safe certificate signature OIDs from a file.
        /// </summary>
        /// <param name="netConfig">The network configuration containing the OQS provider path.</param>
        /// <returns>A list of OIDs. Returns empty list if file is missing.</returns>
        public static List<string> GetCertificateOidAllowList(NetConnectConfig netConfig)
        {
            return GetCertificateOidAllowList(netConfig.OqsProviderPath);
        }

        public static List<string> GetCertificateOidAllowList(string oqsProviderPath)
        {
            return GetCertificateOidNameMap(oqsProviderPath).Keys.ToList();
        }

        /// <summary>
        /// Loads quantum-safe certificate signature OIDs and their algorithm names from a file.
        /// </summary>
        /// <param name="oqsProviderPath">The path to the OQS provider assets.</param>
        /// <returns>A mapping from OID to algorithm name. Returns empty map if file is missing.</returns>
        public static Dictionary<string, string> GetCertificateOidNameMap(string oqsProviderPath)
        {
            var filePath = Path.Combine(oqsProviderPath, "cert_oids");
            if (!File.Exists(filePath)) return new Dictionary<string, string>(StringComparer.Ordinal);

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rawLine in File.ReadAllLines(filePath))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;

                string oid;
                string name;

                var pipeIndex = line.IndexOf('|');
                if (pipeIndex >= 0)
                {
                    oid = line.Substring(0, pipeIndex).Trim();
                    name = line.Substring(pipeIndex + 1).Trim();
                }
                else
                {
                    var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                    oid = parts.Length > 0 ? parts[0].Trim() : "";
                    name = parts.Length > 1 ? parts[1].Trim() : "";
                }

                if (string.IsNullOrWhiteSpace(oid)) continue;
                if (string.IsNullOrWhiteSpace(name)) name = oid;

                map[oid] = name;
            }

            return map;
        }
    }
}
