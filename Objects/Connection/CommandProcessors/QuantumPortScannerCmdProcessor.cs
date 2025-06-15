using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using System.Text;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Connection
{
    public class QuantumPortScannerCmdProcessor : QuantumTestBase
    {
        private readonly string _nmapPath;
        private const int _nmapTimeout = 30000;

        public QuantumPortScannerCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig,
                  "quantum-scan", "Quantum Security Scanner")
        {
            _nmapPath = Path.Combine(netConfig.CommandPath, "nmap");

           }

        public override async Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                    return new ResultObj { Message = "Quantum security scanner not available" };
                if (!File.Exists(_nmapPath))
                    return new ResultObj { Message = $"Nmap executable not found at {_nmapPath}" };
                
                var parsedArgs = base.ParseArguments(arguments);
                var target = GetPositionalArgument(arguments);
  target = target.Replace("https://", "");
                target = target.Replace("http://", "");
                if (string.IsNullOrEmpty(target))
                    return new ResultObj { Message = "Missing required target parameter" };

                var config = new QuantumScanConfig(
                    Target: target,
                    Ports: parsedArgs.GetList("ports", new List<string>()).Select(int.Parse).ToList(),
                    Algorithms: parsedArgs.GetList("algorithms", GetDefaultAlgorithms()),
                    Timeout: parsedArgs.GetInt("timeout", _defaultTimeout),
                    NmapOptions: parsedArgs.GetString("nmap-options", "-T4 --open")
                );

                return await ExecuteFullScan(config, cancellationToken);
            }
            catch (Exception ex)
            {
                return new ResultObj { Message = $"Quantum scan failed: {ex.Message}" };
            }
        }

        private async Task<ResultObj> ExecuteFullScan(QuantumScanConfig config, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(config.Timeout);

            try
            {
                var ports = config.Ports.Any()
                    ? config.Ports
                    : await RunNmapScan(config.Target, config.NmapOptions, cts.Token);

                if (!ports.Any())
                    return new ResultObj { Message = "No open ports found to scan" };

                var results = new List<PortResult>();
                var semaphore = new SemaphoreSlim(_maxParallelTests);

                foreach (var port in ports)
                {
                    await semaphore.WaitAsync(cts.Token);
                    results.Add(await Task.Run(async () =>
                    {
                        try
                        {
                            var portConfig = new QuantumTestConfig(
                                Target: config.Target,
                                Port: port,
                                Algorithms: config.Algorithms,
                                Timeout: config.Timeout / ports.Count
                            );

                            var portResult = await base.ExecuteQuantumTest(portConfig, cts.Token);
                            return new PortResult(port, portResult);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cts.Token));
                }

                return ProcessScanResults(results);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return new ResultObj { Message = $"Full scan timed out after {config.Timeout}ms" };
            }
        }

        private async Task<List<int>> RunNmapScan(string target, string nmapOptions, CancellationToken ct)
        {
            var outputFile = Path.GetTempFileName();
            var arguments = $"{nmapOptions} -oX {outputFile} {target}";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _nmapPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => LogAndCapture(e.Data, output);
            process.ErrorDataReceived += (_, e) => LogAndCapture(e.Data, output);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (ct.Register(() =>
            {
                if (!process.HasExited)
                    process.Kill();
            }))
            {
                await process.WaitForExitAsync(ct);
            }

            if (process.ExitCode != 0)
                throw new Exception($"Nmap scan failed: {output}");

            return ParseNmapOutput(outputFile);
        }

     private List<int> ParseNmapOutput(string xmlPath)
{
    var doc = XDocument.Load(xmlPath);
    return doc.Descendants("port")
        .Where(p => p.Element("state")?.Attribute("state")?.Value == "open")
        .Select(p => p.Attribute("portid")?.Value) // Get the string value safely
        .Where(portid => int.TryParse(portid, out _)) // Ensure it's a valid int
        .Select(portid => int.Parse(portid!)) // Parse safely
        .ToList();
}


        private ResultObj ProcessScanResults(List<PortResult> results)
        {
            var successResults = results.Where(r => r.QuantumResult.Success).ToList();
            var failedResults = results.Where(r => !r.QuantumResult.Success).ToList();
            var output = new StringBuilder();

            if (successResults.Any())
            {
                output.AppendLine("Quantum-safe ports found:");
                foreach (var result in successResults)
                {
                    output.AppendLine($"Port {result.Port}: {result.QuantumResult.Message}");
                    if (result.QuantumResult.Data is string data && !string.IsNullOrEmpty(data))
                    {
                        output.AppendLine($"  Quantum TLS Info: {data}");
                    }
                }
            }

            if (failedResults.Any())
            {
                output.AppendLine("Ports that failed quantum-safe handshake:");
                foreach (var result in failedResults)
                {
                    output.AppendLine($"Port {result.Port}: {result.QuantumResult.Message}");
                }
            }

            if (!successResults.Any() && !failedResults.Any())
            {
                output.AppendLine("No ports were scanned or no results were returned.");
            }

            return new ResultObj
            {
                Success = successResults.Any(),
                Message = output.ToString(),
                Data = results
            };
        }

        public override string GetCommandHelp()
        {
            var baseHelp = base.GetCommandHelp();
            return $@"
{baseHelp}

Additional Nmap Options:
  --nmap-options Custom Nmap options (default: ""-T4 --open"")

Examples:
  example.com --ports 443,8443
  example.com --nmap-options ""-T4 -p 1-1000"" --timeout 120000
";
        }

        private record QuantumScanConfig(
            string Target,
            List<int> Ports,
            List<string> Algorithms,
            int Timeout,
            string NmapOptions);

        private record PortResult(int Port, ResultObj QuantumResult);
    }
}