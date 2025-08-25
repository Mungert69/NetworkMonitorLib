using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Connection;
using NetworkMonitor.Utils;
using NetworkMonitor.Utils.Helpers;
using System.Xml.Linq;
using System.IO;
using System.Threading;
using NetworkMonitor.Service.Services.OpenAI;
using System.Text;

namespace NetworkMonitor.Connection
{
    public class OpensslCmdProcessor : CmdProcessor
    {

        public OpensslCmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
: base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {

        }
        public override async Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj();
            string output = "";
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    _logger.LogWarning($" Warning : {_cmdProcessorStates.CmdDisplayName} is not enabled or installed on this agent.");
                    output = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent. Try installing the Quantum Secure Agent or select an agent that has Openssl enabled.\n";
                    result.Message = await SendMessage(output, processorScanDataObj);
                    result.Success = false;
                    return result;

                }

                string exePath = _netConfig.CommandPath;
                string workingDirectory = _netConfig.CommandPath;
                string opensslPath = Path.Combine(exePath, "openssl");
                 string oqsProviderPath = _netConfig.OqsProviderPath;
           
                if (_netConfig.NativeLibDir != string.Empty)
                {
                    exePath = _netConfig.NativeLibDir;
                    workingDirectory = _netConfig.CommandPath;
                    LibraryHelper.SetLDLibraryPath(_netConfig.NativeLibDir);
                    opensslPath = Path.Combine(_netConfig.NativeLibDir, "openssl-exe.so");
                }
                using (var process = new Process())
                {
                    process.StartInfo.FileName = opensslPath;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true; // Add this to capture standard error
                    process.StartInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = oqsProviderPath;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.WorkingDirectory = workingDirectory;

                    var outputBuilder = new StringBuilder();
                    var errorBuilder = new StringBuilder();

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                        {
                            errorBuilder.AppendLine(e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // Register a callback to kill the process if cancellation is requested
                    using (cancellationToken.Register(() =>
                    {
                        if (!process.HasExited)
                        {
                            _logger.LogInformation($"Cancellation requested, killing the {_cmdProcessorStates.CmdDisplayName} process...");
                            process.Kill();
                        }
                    }))
                    {
                        // Wait for the process to exit or the cancellation token to be triggered
                        await process.WaitForExitAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested(); // Check if cancelled before processing output

                        output = outputBuilder.ToString();
                        string errorOutput = errorBuilder.ToString();

                        if (!string.IsNullOrWhiteSpace(errorOutput) && processorScanDataObj != null)
                        {
                            output = $"RedirectStandardError : {errorOutput}. \n RedirectStandardOutput : {output}";
                        }
                        result.Success = true;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}");
                output += $"Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}\n";
                result.Success = false;
            }
            result.Message = output;
            return result;
        }

        public override string GetCommandHelp()
        {
            return @"
The OpenSSL command processor allows you to test SSL/TLS connections to servers. 
It is designed to verify certificates, protocols, and handshake behavior. 
This processor does not handle certificate creation or management tasks.

### Usage:

Arguments:
- The arguments should include a valid OpenSSL command for testing server connections.
- Common commands include:
  - `s_client`: Connects to a server to test SSL/TLS.
  - `x509`: Prints information about a certificate.

### Examples:

1. **Test SSL/TLS handshake for a specific server**:

openssl s_client -connect example.com:443

This checks the SSL/TLS handshake with `example.com` on port 443.

2. **Specify a particular protocol (e.g., TLSv1.2)**:

openssl s_client -connect example.com:443 -tls1_2

This forces the handshake to use TLS version 1.2.

3. **Verify the certificate chain**:

openssl s_client -connect example.com:443 -showcerts

This retrieves and displays the server's certificate chain.

4. **Check for specific ciphers**:

openssl s_client -connect example.com:443 -cipher AES256-SHA

This tests whether the server supports the specified cipher suite.

5. **Export server certificate to a file**:

openssl s_client -connect example.com:443 </dev/null | openssl x509 -out server-cert.pem

This extracts the server's certificate and saves it to `server-cert.pem`.

### Notes:
- Ensure the `openssl` binary is correctly installed and configured on the agent.
- You can set the `LD_LIBRARY_PATH` environment variable to use custom OpenSSL libraries.
- If you encounter issues with specific commands, check that the required OpenSSL modules are available.

### Troubleshooting:
1. **Error: 'command not available'**:
- This indicates that OpenSSL is not installed or the `LD_LIBRARY_PATH` is not correctly set. Ensure OpenSSL is installed and configured.

2. **Handshake failures**:
- Check the server's SSL/TLS configuration and supported protocols. You can use the `-tls1_2`, `-tls1_3`, etc., flags to enforce specific protocols.

3. **Certificate validation errors**:
- Use the `-CAfile` or `-CApath` options to specify a custom CA certificate if the default is insufficient.

This processor simplifies the use of OpenSSL for connection testing, providing an efficient way to diagnose server-side SSL/TLS issues.
";
        }


    }



}