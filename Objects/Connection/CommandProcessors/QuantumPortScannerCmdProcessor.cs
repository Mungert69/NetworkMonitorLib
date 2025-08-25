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
using NetworkMonitor.Utils.Helpers;
using System.Text;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Connection
{
    public class QuantumPortScannerCmdProcessor : QuantumTestBase
    {
        private readonly string _nmapPath;
        private const int _nmapTimeout = 30000;
        const int MaxPortsToScan = 20;


        public QuantumPortScannerCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig,
                  "quantum-scan", "Quantum Security Scanner", queueLength: 10)
        {

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

                // Don't supply default here, so you can detect empty:
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

                var config = new QuantumScanConfig(
                    Target: target,
                    Ports: parsedArgs.GetList("ports", new List<string>()).Select(int.Parse).ToList(),
                    Algorithms: algosToUse,
                    Timeout: parsedArgs.GetInt("timeout", _defaultTimeout),
                    NmapOptions: parsedArgs.GetString("nmap-options", "-T4 --open"),
                    batch: batch
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
            // Outer token for the entire scan phase (OpenSSL loop)

            List<int> ports;
            try
            {
                // If ports not specified, run nmap with its own dedicated timeout!
                if (config.Ports.Any())
                {
                    ports = config.Ports;
                }
                else
                {
                    // Separate timeout just for nmap!
                    using var nmapCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    nmapCts.CancelAfter(_nmapTimeout);
                    try
                    {
                        ports = await RunNmapScan(config.Target, config.NmapOptions, nmapCts.Token);
                    }
                    catch (TimeoutException)
                    {
                        return new ResultObj
                        {
                            Success = false,
                            Message = $"Nmap scan exceeded timeout of {_nmapTimeout / 1000} seconds. Please specify a smaller port list (e.g., with --ports 443,8443)."
                        };
                    }
                }

                if (!ports.Any())
                    return new ResultObj { Success = false, Message = "No open ports found to scan" };

                if (ports.Count > MaxPortsToScan)
                {
                    return new ResultObj
                    {
                        Success = false,
                        Message = $"Too many ports specified ({ports.Count}). The maximum allowed is {MaxPortsToScan}. " +
                                  $"Please specify a smaller port list (e.g., with --ports 443,8443)."
                    };
                }

                var results = new List<PortResult>();
                var semaphore = new SemaphoreSlim(_maxParallelTests);
                var portTasks = new List<Task<PortResult>>();

                foreach (var port in ports)
                {
                    await semaphore.WaitAsync(ct);

                    portTasks.Add(Task.Run(async () =>
                    {
                        using var portCts = new CancellationTokenSource(config.Timeout);

                        try
                        {
                            var portConfig = new QuantumTestConfig(
                                Target: config.Target,
                                Port: port,
                                Algorithms: config.Algorithms,
                                Timeout: config.Timeout
                            );

                            List<AlgorithmResult> algoResults;
                            if (config.batch)
                            {
                                // Batch mode: single OpenSSL call for all algos
                                algoResults = await base.ProcessAlgorithmGroup(portConfig, config.Algorithms, portCts.Token);
                            }
                            else
                            {
                                // One-by-one mode
                                algoResults = await base.ProcessAlgorithmGroupOneByOne(portConfig, config.Algorithms, portCts.Token);
                            }

                            // Combine/flatten algoResults for port (your existing logic for summarizing can remain)
                            var portResult = base.ProcessTestResults(algoResults);
                            return new PortResult(port, portResult);
                        }
                        catch (OperationCanceledException)
                        {
                            return new PortResult(port, new ResultObj
                            {
                                Success = false,
                                Message = $"Port scan for {port} timed out.",
                                Data = null
                            });
                        }
                        catch (Exception ex)
                        {
                            return new PortResult(port, new ResultObj
                            {
                                Success = false,
                                Message = $"Port scan for {port} failed: {ex.Message}",
                                Data = null
                            });
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                // Wait for all scans to finish (whether completed or timed out)
                var completedResults = await Task.WhenAll(portTasks);

                return ProcessScanResults(completedResults.ToList());


            }
            catch (Exception e)
            {
                return new ResultObj { Message = $"Quantum scan failed: {e.Message}" };
            }
        }

        private async Task<List<int>> RunNmapScan(string target, string nmapOptions, CancellationToken ct)
        {
            var outputFile = Path.GetTempFileName();
            var arguments = $"{nmapOptions} -oX {outputFile} {target}";

            string exePath = _netConfig.CommandPath;
            string workingDirectory = _netConfig.CommandPath;
            string dataDir = "";
            string nmapPath = Path.Combine(exePath, "nmap");
            if (_netConfig.NativeLibDir != string.Empty)
            {
                exePath = _netConfig.NativeLibDir;
                workingDirectory = _netConfig.CommandPath;
                dataDir = " --datadir " + _netConfig.CommandPath;
                LibraryHelper.SetLDLibraryPath(_netConfig.NativeLibDir);
                nmapPath = Path.Combine(_netConfig.NativeLibDir, "nmap-exe.so");
            }

            using var process = new Process();
            process.StartInfo.FileName = nmapPath;
            process.StartInfo.Arguments = arguments  + dataDir;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WorkingDirectory = workingDirectory;


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
            string NmapOptions,
            bool batch);

        private record PortResult(int Port, ResultObj QuantumResult);
    }
}