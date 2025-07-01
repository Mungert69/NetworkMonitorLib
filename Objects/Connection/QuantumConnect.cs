using NetworkMonitor.Objects;
using System.Text;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Reflection;
namespace NetworkMonitor.Connection
{
    public class QuantumConnect : NetConnect
    {
        private readonly List<string> _oqsCodepoints = new List<string>();
        private readonly List<string> _curves = new List<string>();
        private List<AlgorithmInfo> _algorithmInfoList;
        private string _oqsProviderPath = "";
        private string _commandPath = "";
        private ILogger _logger;

        public QuantumConnect(List<AlgorithmInfo> algorithmInfoList, string oqsProviderPath, string commandPath, ILogger logger)
        {
            _logger = logger;
            _algorithmInfoList = algorithmInfoList;
            _oqsProviderPath = oqsProviderPath;
            _commandPath = commandPath;


            IsLongRunning = true;
        }
        public override async Task Connect()
        {
            PreConnect();
            int port = 443;
            // if MonitorPingInfo port is zero then use https default port
            if (MpiStatic.Port != 0)
                port = MpiStatic.Port;
            try
            {
                // Time the IsQuantumSafe method
                //Timer.Start();
                Timer.Reset();
                Timer.Start();
                var result = await IsQuantumSafe(MpiStatic.Address, port);
                Timer.Stop();
                if (result.Success)
                {
                    ProcessStatus("Using quantum safe handshake", (ushort)Timer.ElapsedMilliseconds, " : " + result.Data);

                    //Console.WriteLine("Using quantum safe handshake : " + quantumSafe.Item2);
                }
                else
                {
                    MpiConnect.Message = "Could not negotiate quantum safe handshake : " + result.Message;
                    ProcessException("Could not negotiate quantum safe handshake", "Could not negotiate quantum safe handshake");
                }
            }
            catch (OperationCanceledException)
            {
                ProcessException("Timeout", "Timeout");
            }
            catch (Exception e)
            {
                ProcessException(e.Message, "Exception");
            }
            finally
            {
                PostConnect();
            }
        }
        public async Task<ResultObj> ProcessAlgorithm(AlgorithmInfo algorithmInfo, string address, int port)
        {
            var result = new ResultObj();

            //Console.WriteLine($"Running algorithm: {algorithmInfo.AlgorithmName}");
            string output = await RunCommandAsync(algorithmInfo.EnvironmentVariable + "=" + algorithmInfo.DefaultID, algorithmInfo.AlgorithmName, address, port, algorithmInfo.AddEnv);
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
        private async Task<string> RunCommandAsync(string oqsCodepoint, string curve, string address, int port, bool addEnv)
        {
            StringBuilder output = new StringBuilder();
            StringBuilder error = new StringBuilder();
            var env = new Dictionary<string, string>();
            string exeName = "openssl";

            string opensslExecutable = Path.Combine(_commandPath, exeName);



            var processStartInfo = new ProcessStartInfo
            {
                FileName = opensslExecutable,
                Arguments = $"s_client -curves {curve} -connect {address}:{port} -provider-path {_oqsProviderPath} -provider oqsprovider -provider default -msg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(opensslExecutable) // Set the correct working directory

            };
            if (addEnv)
            {
                processStartInfo.EnvironmentVariables.Add(oqsCodepoint.Split('=')[0], oqsCodepoint.Split('=')[1]);
            }
            Console.WriteLine(processStartInfo.FileName.ToString() + " " + processStartInfo.Arguments.ToString());
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Set the PATH environment variable to include the OpenSSL library path for Windows
                string currentPath = processStartInfo.EnvironmentVariables["PATH"] ?? "";
                processStartInfo.EnvironmentVariables["PATH"] = _oqsProviderPath + ";" + currentPath;
            }
            else
            {
                // Set the LD_LIBRARY_PATH environment variable for Linux
                processStartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = _oqsProviderPath;
            }

            using var process = new Process { StartInfo = processStartInfo };
            Cts.Token.Register(() =>
                 {
                     try
                     {
                         _logger.LogDebug("PROCESSWAIT Timeout occurred for : " + address);

                         if (process != null && !process.HasExited)
                         {
                             if (output.ToString().Contains("ServerHello"))
                                 _logger.LogDebug("PROCESSWAIT : Got SERVERHELLO : Timeout occurred for : " + address + " Process has not exited so killing process.");//  Output was : " + output.ToString());
                             process.Kill(false);
                         }
                     }
                     catch (Exception ex)
                     {
                         _logger.LogDebug("PROCESSWAIT Exception occurred for : " + address + " Exception was : " + ex.Message);
                     }

                 });
            process.Start();
            using StreamReader sr = process.StandardOutput;
            using StreamReader srError = process.StandardError;
            // Define the timeout duration
            int timeoutMilliseconds = MpiStatic.Timeout;
            var outputTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    Cts.Token.ThrowIfCancellationRequested();
                    output.AppendLine(line);
                    _logger.LogDebug(line);
                    if (line.Contains("Finished"))
                    {
                        break;
                    }
                }
            }, Cts.Token);
            var errorTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await srError.ReadLineAsync()) != null)
                {
                    _logger.LogDebug(line);
                    Cts.Token.ThrowIfCancellationRequested();
                    error.AppendLine(line);
                }
            }, Cts.Token);
            try
            {
                await Task.WhenAll(outputTask, errorTask);
            }
            catch (OperationCanceledException)
            {
                sr.Close();
                srError.Close();
            }
            process.WaitForExit(MpiStatic.Timeout);

            return $"{error.ToString()} : {output.ToString()}";
        }
    }
}
