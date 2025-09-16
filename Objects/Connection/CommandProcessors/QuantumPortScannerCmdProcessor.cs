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
using NetworkMonitor.Utils;

namespace NetworkMonitor.Connection
{
    public class QuantumPortScannerCmdProcessor : QuantumTestBase
    {
        private const int MaxPortsToScan = 20;

        private readonly List<ArgSpec> _schema;

        public QuantumPortScannerCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig,
                  "quantum-scan", "Quantum Security Scanner", queueLength: 10)
        {
            _schema = new()
            {
                // Optional so we still support positional target
                new()
                {
                    Key = "target",
                    Required = true,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Hostname (or URL). If omitted, the first positional token is used."
                },
                new()
                {
                    Key = "algorithms",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Algorithms list. Supports comma, space, semicolon, or colon separators."
                },
                new()
                {
                    Key = "ports",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Explicit ports list (e.g., \"443,8443\"). If omitted, nmap is used."
                },
                new()
                {
                    Key = "timeout",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = _defaultTimeout.ToString(),
                    Help = "Per-port scan timeout in ms."
                },
                new()
                {
                    Key = "nmap_options",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "value",
                    DefaultValue = "-T4 --open",
                    Help = "Custom nmap options (used when --ports not provided)."
                }
            };
        }

        public override async Task<ResultObj> RunCommand(
            string arguments,
            CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                    return new ResultObj { Success = false, Message = "Quantum security scanner not available" };

                // Parse args with validation for ints + defaults
                var parsed = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
                if (!parsed.Success)
                {
                    var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parsed, _schema);
                    _logger.LogWarning("Arguments not valid {args}. {msg}", arguments, parsed.Message);
                    return new ResultObj { Success = false, Message = err };
                }

                // Resolve target (support both --target and positional)
                var target = parsed.GetString("target");
                if (string.IsNullOrWhiteSpace(target))
                {
                    target = GetPositionalArgument(arguments);
                }
                target = NormalizeTarget(target);

                if (string.IsNullOrWhiteSpace(target))
                {
                    return new ResultObj { Success = false, Message = "Missing required target.\n\n" + GetCommandHelp() };
                }

                // Algorithms: flexible separators; no validation/aliasing
                var algoRaw = parsed.GetString("algorithms");
                var userAlgos = ParseList(algoRaw);

                List<string> algosToUse;
                bool batch;
                if (userAlgos.Count == 0)
                {
                    algosToUse = GetDefaultAlgorithms();
                    batch = true;
                }
                else
                {
                    algosToUse = userAlgos;
                    batch = false;
                }

                // Ports: flexible separators; integers only
                var portsRaw = parsed.GetString("ports");
                var (ports, portError) = ParsePorts(portsRaw);
                if (!string.IsNullOrEmpty(portError))
                {
                    return new ResultObj { Success = false, Message = portError + "\n\n" + GetCommandHelp() };
                }

                var config = new QuantumScanConfig(
                    Target: target,
                    Ports: ports,
                    Algorithms: algosToUse,
                    Timeout: parsed.GetInt("timeout", _defaultTimeout),
                    NmapOptions: parsed.GetString("nmap_options", "-T4 --open"),
                    batch: batch
                );

                return await ExecuteFullScan(config, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new ResultObj { Success = false, Message = "Quantum scan canceled or timed out.\n" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quantum scan failed");
                return new ResultObj { Success = false, Message = $"Quantum scan failed: {ex.Message}" };
            }
        }

        private async Task<ResultObj> ExecuteFullScan(QuantumScanConfig config, CancellationToken ct)
        {
            try
            {
                // 1) Resolve the port list
                List<int> ports;
                if (config.Ports.Any())
                {
                    ports = config.Ports;
                }
                else
                {
                    // Use nmap (with its own timeout)
                    using var nmapCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    nmapCts.CancelAfter(_defaultTimeout);
                    try
                    {
                        ports = await RunNmapScan(config.Target, config.NmapOptions, nmapCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return new ResultObj
                        {
                            Success = false,
                            Message = $"Nmap scan exceeded timeout of {_defaultTimeout / 1000} seconds. Please specify a smaller port list (e.g., with --ports 443,8443)."
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

                // 2) Per-port scans (parallel with cap)
                var results = new List<PortResult>();
                var semaphore = new SemaphoreSlim(_maxParallelTests);
                var tasks = new List<Task<PortResult>>();

                foreach (var port in ports)
                {
                    await semaphore.WaitAsync(ct);

                    tasks.Add(Task.Run(async () =>
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

                            List<AlgorithmResult> algoResults = config.batch
                                ? await base.ProcessAlgorithmGroup(portConfig, config.Algorithms, portCts.Token)
                                : await base.ProcessAlgorithmGroupOneByOne(portConfig, config.Algorithms, portCts.Token);

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
                    }, ct));
                }

                var completed = await Task.WhenAll(tasks);
                return ProcessScanResults(completed.ToList());
            }
            catch (Exception e)
            {
                return new ResultObj { Success = false, Message = $"Quantum scan failed: {e.Message}" };
            }
        }

        private async Task<List<int>> RunNmapScan(string target, string nmapOptions, CancellationToken ct)
        {
            var outputFile = Path.GetTempFileName();
            var env = new NmapEnvironment(_netConfig, _logger);
            var runner = env.CreateRunner(_logger);

            var (success, output) = await runner.RunAsync(
                env.NmapPath,
                $"{nmapOptions} -oX {outputFile} {target} {env.DataDir}",
                ct
            );

            if (!success)
                throw new Exception($"Nmap scan failed: {output}");

            return ParseNmapOutput(outputFile);
        }

        private static void LogAndCapture(string? line, StringBuilder sb)
        {
            if (string.IsNullOrEmpty(line)) return;
            sb.AppendLine(line);
        }

        private List<int> ParseNmapOutput(string xmlPath)
        {
            var doc = XDocument.Load(xmlPath);
            return doc.Descendants("port")
                .Where(p => p.Element("state")?.Attribute("state")?.Value == "open")
                .Select(p => p.Attribute("portid")?.Value)
                .Where(portid => int.TryParse(portid, out _))
                .Select(portid => int.Parse(portid!))
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

        public override string GetCommandHelp() => @"
Scan a host for quantum-ready TLS across one or more ports.

Usage:
  --target <host|url> [--algorithms <list>] [--ports <list>] [--timeout <ms>] [--nmap_options ""...""]
  (You may also pass the target as the first positional argument.)

Arguments:
  --target          Hostname or URL. If a URL is passed, the scheme is stripped.
  --algorithms      Algorithms list; separators: comma, space, semicolon, or colon.
                    Examples:
                      --algorithms mlkem768
                      --algorithms ""mlkem512, mlkem768, p256_mlkem512""
                      --algorithms x25519_mlkem512 p384_mlkem768
                      --algorithms ""frodo640aes:frodo640shake; p256_frodo640aes x25519_frodo640shake""
  --ports           Explicit ports (same separators). Example: ""443,8443""
                    If omitted, nmap is used to discover open ports.
  --timeout         Per-port scan timeout in ms (default from config).
  --nmap_options    Options passed to nmap when --ports is omitted (default: ""-T4 --open"").

Required:
 only --target is required.

Notes:
  • At most 20 ports may be scanned per invocation.
  • If nmap discovery times out, try specifying a smaller port list via --ports.";

        // ---------- helpers ----------

        private static string NormalizeTarget(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = s.Trim();
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) t = t.Substring("http://".Length);
            else if (t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) t = t.Substring("https://".Length);
            if (t.EndsWith("/")) t = t.TrimEnd('/');
            return t;
        }

        private static List<string> ParseList(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new();
            var tokens = raw.Split(new[] { ',', ';', ':', ' ', '\t', '\r', '\n' },
                                   StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var tok in tokens)
            {
                var v = tok.Trim();
                if (v.Length == 0) continue;
                if (seen.Add(v)) result.Add(v);
            }
            return result;
        }

        private static (List<int> ports, string? error) ParsePorts(string? raw)
        {
            var ports = new List<int>();
            if (string.IsNullOrWhiteSpace(raw)) return (ports, null);

            var tokens = raw.Split(new[] { ',', ';', ':', ' ', '\t', '\r', '\n' },
                                   StringSplitOptions.RemoveEmptyEntries);

            foreach (var tok in tokens)
            {
                if (!int.TryParse(tok.Trim(), out var p) || p < 1 || p > 65535)
                {
                    return (new List<int>(), $"Invalid port '{tok}'. Ports must be integers between 1 and 65535.");
                }
                ports.Add(p);
            }

            // de-dupe, preserve order
            var seen = new HashSet<int>();
            var dedup = new List<int>();
            foreach (var p in ports)
            {
                if (seen.Add(p)) dedup.Add(p);
            }

            return (dedup, null);
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
