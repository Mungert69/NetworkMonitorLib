using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        /// Loads algorithm information from a CSV file and enables algorithms based on a curves file.
        /// </summary>
        /// <param name="netConfig">The network configuration containing the paths to the CSV and curves files.</param>
        /// <returns>A list of AlgorithmInfo objects with enabled/disabled status.</returns>
        public static List<AlgorithmInfo> GetAlgorithmInfoList(NetConnectConfig netConfig)
        {
            // Load algorithm information from the CSV file
            string csvFilePath = Path.Combine(netConfig.OqsProviderPath, "AlgoTable.csv");
            var algorithmInfoList = CsvParser.ParseAlgorithmInfoCsv(csvFilePath);

            // Load the list of supported curves
            List<string> groups = File.ReadAllLines(Path.Combine(netConfig.OqsProviderPath, "curves")).ToList();

            // Enable algorithms that are in the curves file
            foreach (AlgorithmInfo algoInfo in algorithmInfoList)
            {
                algoInfo.Enabled = groups.Contains(algoInfo.AlgorithmName);
            }

            return algorithmInfoList;
        }

        /// <summary>
        /// Loads quantum-safe certificate signature OIDs from a file (one OID per line).
        /// </summary>
        /// <param name="netConfig">The network configuration containing the OQS provider path.</param>
        /// <returns>A list of OIDs. Returns empty list if file is missing.</returns>
        public static List<string> GetCertificateOidAllowList(NetConnectConfig netConfig)
        {
            return GetCertificateOidAllowList(netConfig.OqsProviderPath);
        }

        public static List<string> GetCertificateOidAllowList(string oqsProviderPath)
        {
            var filePath = Path.Combine(oqsProviderPath, "cert_oids");
            if (!File.Exists(filePath)) return new List<string>();

            return File.ReadAllLines(filePath)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal))
                .ToList();
        }
    }
}
