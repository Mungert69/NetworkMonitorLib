using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using System.Text;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;

namespace NetworkMonitor.Connection
{
    public class QuantumConnect : NetConnect
    {
        private readonly List<AlgorithmInfo> _algorithmInfoList;
        private string _oqsProviderPath;
        private string _commandPath;
        private string _nativeLibDir = string.Empty;
        private readonly ILogger _logger;

        public QuantumConnect(
            List<AlgorithmInfo> algorithmInfoList,
            string oqsProviderPath,
            string commandPath,
            ILogger logger, string nativeLibDir = "")
        {
            _algorithmInfoList = algorithmInfoList;
            _nativeLibDir = nativeLibDir;
            _oqsProviderPath = oqsProviderPath;
            _commandPath = commandPath;
            _logger = logger;


            IsLongRunning = true;
        }

        /*───────────────────────────  ★ THE SINGLE SEAM ★  ───────────────────────────*/

        /// <summary>
        /// Executes the OpenSSL/OQS process.  
        /// Override in tests to return canned output; production keeps default logic.
        /// </summary>
        protected virtual async Task<string> RunCommandAsync(
            string oqsCodepoint,
            string curve,
            string address,
            int port,
            bool addEnv,
            CancellationToken token)
        {
            var output = new StringBuilder();
            var error = new StringBuilder();
            string opensslPath = Path.Combine(_commandPath, "openssl");
            if (!string.IsNullOrEmpty(_nativeLibDir))
            {
                // Use the native library directory if provided
                _commandPath = _nativeLibDir;
                _oqsProviderPath = _nativeLibDir;
                LibraryHelper.SetLDLibraryPath(_nativeLibDir);
                opensslPath = Path.Combine(_commandPath, "openssl.so");
            }


            var psi = new ProcessStartInfo
            {
                FileName = opensslPath,
                Arguments = $"s_client -curves {curve} -connect {address}:{port} "
                                        + $"-provider-path {_oqsProviderPath} -provider oqsprovider "
                                        + "-provider default -msg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _commandPath
            };

            // Log the full command and arguments
            _logger.LogInformation($"Running command: {psi.FileName} {psi.Arguments}");

            if (addEnv)
            {
                var kv = oqsCodepoint.Split('=', 2);
                psi.EnvironmentVariables[kv[0]] = kv[1];
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi.EnvironmentVariables["PATH"] =
                    _oqsProviderPath + ";" + (psi.EnvironmentVariables["PATH"] ?? "");
            }
            else
            {
                psi.EnvironmentVariables["LD_LIBRARY_PATH"] = _oqsProviderPath;
            }

            using var proc = new Process { StartInfo = psi };

            token.Register(() =>
            {
                try
                {
                    if (!proc.HasExited) proc.Kill();
                }
                catch { /* ignore */ }
            });

            proc.Start();

            await Task.WhenAll(
                ReadStreamAsync(proc.StandardOutput, output, token),
                ReadStreamAsync(proc.StandardError, error, token));

            proc.WaitForExit();
            _logger.LogDebug($"Output: {error} : {output}");

            return $"{error} : {output}";
        }

        private static async Task ReadStreamAsync(
            StreamReader reader, StringBuilder sb, CancellationToken ct)
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();
                sb.AppendLine(line);
            }
        }


        public override async Task Connect()
        {
            PreConnect();
            int port = MpiStatic.Port != 0 ? MpiStatic.Port : 443;

            try
            {
                Timer.Reset(); Timer.Start();
                var r = await IsQuantumSafe(MpiStatic.Address, port);
                Timer.Stop();

                if (r.Success)
                    ProcessStatus("Using quantum safe handshake",
                                  (ushort)Timer.ElapsedMilliseconds,
                                  " : " + r.Data);
                else
                    ProcessException("Could not negotiate quantum safe handshake",
                                     "Could not negotiate quantum safe handshake");
            }
            catch (OperationCanceledException)
            {
                ProcessException("Timeout", "Timeout");
            }
            catch (Exception ex)
            {
                ProcessException(ex.Message, "Exception");
            }
            finally { PostConnect(); }
        }

        public async Task<ResultObj> ProcessAlgorithm(AlgorithmInfo algorithmInfo, string address, int port)
        {
            var result = new ResultObj();

            //Console.WriteLine($"Running algorithm: {algorithmInfo.AlgorithmName}");
            string output = await RunCommandAsync(algorithmInfo.EnvironmentVariable + "=" + algorithmInfo.DefaultID, algorithmInfo.AlgorithmName, address, port, algorithmInfo.AddEnv, Cts.Token);
            string[] successIndicators = new[] {
                        "ServerHello",
                        //"EncryptedExtensions",
                        "Certificate",
                        //"CertificateVerify",
                        //"Finished"
                        };
            var foundIndicators = successIndicators.Where(indicator => output.Contains(indicator)).ToList();
            bool successfulHandshake = foundIndicators.Count == successIndicators.Length;
            if (successfulHandshake)
            {
                // Console.WriteLine("The handshake was successful.");
                var serverHelloHelper = new ServerHelloHelper();
                KemExtension kemExtension = serverHelloHelper.FindServerHello(output);
                //Console.WriteLine("OUTPUT ServerHelloHelper :: "+serverHelloHelper.Sb.ToString());
                if (kemExtension.IsQuantumSafe)
                {
                    result.Success = true;
                    // Use kemExtension.KemName to get the name of the quantum-safe algorithm from _algorithmInfoList
                    var algoNameFromKem = "unknown";
                    foreach (var algorithm in _algorithmInfoList)
                    {
                        if (algorithm.DefaultID == kemExtension.GroupID)
                        {
                            algoNameFromKem = algorithm.AlgorithmName;
                            break;
                        }
                    }
                    result.Data = algoNameFromKem;
                    _logger.LogInformation(" Success : " + address + " : " + result.Data);
                    return result;
                }
                else
                {
                    if (kemExtension.LongServerHello)
                    {
                        _logger.LogError(" Fail with Long ServerHello : " + address + " : " + algorithmInfo.AlgorithmName + " Log is : " + serverHelloHelper.Sb.ToString());
                    }
                }
            }
            else
            {
                //Console.WriteLine("The handshake was not successful.");
            }
            if (output.Contains("connect:errno"))
            {
                result.Success = false;
                result.Data = algorithmInfo.AlgorithmName;
                result.Message = " connect:errno ";
                return result;
            }
            var alertLines = output.Split(Environment.NewLine).Where(line => line.Contains("Alert")).ToList();
            if (alertLines.Any())
            {
                _logger.LogDebug("Alert lines:");
                foreach (string line in alertLines)
                {
                    result.Message += line;
                    _logger.LogDebug($"- {line}");
                }
            }

            result.Success = false;
            result.Data = null;
            return result;
        }
        public async Task<ResultObj> IsQuantumSafe(string address, int port)
        {
            // Try all modern algorithms first (those that _don’t_ need env-vars)
            var modernList = _algorithmInfoList
                             .Where(a => a.Enabled && !a.AddEnv)
                             .Select(a => a.AlgorithmName)
                             .ToList();

            if (modernList.Any())
            {
                var modernResult = await ProcessAlgorithm(
                                       new AlgorithmInfo
                                       {
                                           AlgorithmName = string.Join(':', modernList),
                                           AddEnv = false
                                       },
                                       address, port);

                if (modernResult.Success)          // ← success?  we’re done
                    return modernResult;
            }

            // Legacy / draft-ID algorithms (need env-vars) – one by one
            foreach (var algo in _algorithmInfoList.Where(a => a.Enabled && a.AddEnv))
            {
                var legacyResult = await ProcessAlgorithm(algo, address, port);
                if (legacyResult.Success)
                    return legacyResult;           // first winner short-circuits
            }

            //  Nothing worked
            return new ResultObj
            {
                Success = false,
                Message = "No quantum-safe algorithm negotiated"
            };
        }

        public async Task<ResultObj> ProcessBatchAlgorithms(List<AlgorithmInfo> algorithms, string address, int port)
        {
            var result = new ResultObj();
            if (algorithms == null || algorithms.Count == 0)
            {
                result.Success = false;
                result.Message = "No algorithms to test";
                return result;
            }

            string curves = string.Join(":", algorithms.Select(a => a.AlgorithmName));
            string output = await RunCommandAsync(string.Empty, curves, address, port, false, Cts.Token);

            // ServerHello and KEM extension parsing logic (reuse your existing code)
            var serverHelloHelper = new ServerHelloHelper();
            KemExtension kemExtension = serverHelloHelper.FindServerHello(output);

            if (kemExtension.IsQuantumSafe)
            {
                // Find algorithm name from the negotiated group
                var negotiated = algorithms.FirstOrDefault(a => a.DefaultID == kemExtension.GroupID)?.AlgorithmName
                                 ?? "unknown";
                result.Success = true;
                result.Data = negotiated;
                result.Message = $"Negotiated quantum-safe algorithm: {negotiated}";
                _logger.LogInformation("Success: {Address} : {Negotiated}", address, negotiated);
            }
            else
            {
                result.Success = false;
                result.Message = "No quantum-safe algorithm negotiated";
                _logger.LogWarning("No quantum-safe algorithm negotiated for {Address}", address);
            }

            return result;
        }



    }
}
