using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Connection;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;

namespace NetworkMonitor.Connection
{
    public class MetaCmdProcessor : CmdProcessor
    {


        public MetaCmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
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
                    _logger.LogWarning(" Warning : Metasploit is not enabled or installed on this agent.");
                    output = "Metasploit is not available on this agent. Try installing the docker version of the Quantum Secure Agent or select an agent that has Metasploit Enabled.\n";
                    result.Message = await SendMessage(output, processorScanDataObj);
                    result.Success = false;
                    return result;

                }

                string message = $"Running Metasploit with arguments {arguments}";
                _logger.LogInformation(message);
                _cmdProcessorStates.RunningMessage += $"{message}\n";

                output = await ExecuteMetasploit(arguments, cancellationToken, processorScanDataObj);

                _logger.LogInformation("Metasploit module execution completed.");
                _cmdProcessorStates.CompletedMessage += "Metasploit module execution completed successfully.\n";

                // Process the output (if any additional processing is needed)
                ProcessMetasploitOutput(output);
                result.Message += output;

            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Metasploit module execution was cancelled.");
                _cmdProcessorStates.CompletedMessage += "Metasploit module execution was cancelled.\n";
                result.Message += _cmdProcessorStates.CompletedMessage;
                result.Success = false;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error during Metasploit module execution: {e.Message}");
                _cmdProcessorStates.CompletedMessage += $"Error during Metasploit module execution: {e.Message}\n";
                result.Success = false;
                result.Message += _cmdProcessorStates.CompletedMessage;
            }

            return result;
        }

        public override string GetCommandHelp()
        {
            return @"
The MetaCmdProcessor executes Metasploit commands via `msfconsole`. The `msfconsole` executable is already configured within the processor. 
You only need to provide valid arguments to define the commands, modules, and actions to execute.

### Usage:

- **Arguments**: Provide a string containing the Metasploit commands and their parameters.
- The processor automatically runs `msfconsole` with the supplied arguments and exits after execution.

### Features and Examples:

1. **Run an Exploit Module**:

arguments: '-q -x ""use exploit/windows/smb/ms17_010_eternalblue; set RHOSTS 192.168.1.1; run; exit""'

- Executes the EternalBlue exploit against the target `192.168.1.1`.

2. **Run an Auxiliary Module**:

arguments: '-q -x ""use auxiliary/scanner/http/http_version; set RHOSTS 192.168.1.1; run; exit""'

- Checks the HTTP version on the target system.

3. **Perform an Nmap Scan Using Metasploit**:

arguments: '-q -x ""db_nmap -sV -p 80,443 192.168.1.1; exit""'

- Performs an Nmap scan for services on ports 80 and 443.

4. **Custom Commands**:

arguments: '-q -x ""show exploits; exit""'

- Lists all available exploits in the Metasploit framework.

### Key Notes:

1. **Metasploit Pre-Configuration**:
- The processor automatically runs `msfconsole`. You only need to supply the necessary arguments to define commands or modules.

2. **Command Formatting**:
- Ensure commands follow the correct Metasploit syntax and structure.
- Use `-q` for quiet mode and `-x` to pass commands directly to `msfconsole`.

3. **Execution Context**:
- Each execution is a standalone run. Persistent sessions or interactive features (e.g., `sessions -i`) are not supported.

4. **Environment**:
- Metasploit must be installed and accessible on the agent.
- On Windows, ensure the `msfconsole.bat` is in the configured path.

5. **Examples Summary**:
- Exploit: `'-q -x \""use exploit/windows/smb/ms17_010_eternalblue; set RHOSTS 192.168.1.1; run; exit\""'`
- Auxiliary scan: `'-q -x \""use auxiliary/scanner/http/http_version; set RHOSTS 192.168.1.1; run; exit\""'`
- Nmap: `'-q -x \""db_nmap -sV -p 80,443 192.168.1.1; exit\""'`

### Troubleshooting:

1. **Command Not Found**:
- Ensure Metasploit is installed and in the system PATH.
- Check the agent's configuration for any missing settings.

2. **Error Messages**:
- If commands fail, verify their syntax and compatibility with the Metasploit version in use.

### Limitations:

- **One-Off Commands**:
- Designed for standalone command execution, not persistent interaction or session management.

By providing arguments to this processor, you can execute a wide range of Metasploit features with precision.
";
        }

        private async Task<string> ExecuteMetasploit(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj)
        {
            string msfDir = "";
            string msfPath="";
            _cmdProcessorStates.CmdName="msfconsole";
            string output = "";
            // Use 'where' command to locate the executable in the system's PATH
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                msfDir = await FindExecutableDirectoryInPath(_cmdProcessorStates.CmdName, "where");
                msfPath = Path.Combine(msfDir, _cmdProcessorStates.CmdName) + ".bat";
                if (string.IsNullOrEmpty(msfDir))
                {
                    throw new FileNotFoundException($"Metasploit executable {_cmdProcessorStates.CmdName} not found in system PATH.");
                }
            }
            else
            {
                msfDir = await FindExecutableDirectoryInPath(_cmdProcessorStates.CmdName, "which");
                msfPath = Path.Combine(msfDir, _cmdProcessorStates.CmdName);
                if (string.IsNullOrEmpty(msfDir))
                {
                    throw new FileNotFoundException($"Metasploit executable {_cmdProcessorStates.CmdName} not found in system PATH.");
                }
            }

            using (var process = new Process())
            {
                process.StartInfo.FileName = msfPath;// Path to the Metasploit console executable
                process.StartInfo.Arguments = arguments; // Executes the command

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true; // Add this to capture standard error
                process.StartInfo.WorkingDirectory = msfDir;
                process.StartInfo.CreateNoWindow = true;
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

                using (cancellationToken.Register(() =>
                {
                    if (!process.HasExited)
                    {
                        _logger.LogInformation("Cancellation requested, killing the Metasploit process...");
                        process.Kill();
                    }
                }))
                {
                    await process.WaitForExitAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested(); // Check if cancelled before processing output

                    output = outputBuilder.ToString();
                    string errorOutput = errorBuilder.ToString();

                    if (!string.IsNullOrWhiteSpace(errorOutput) && processorScanDataObj != null)
                    {
                        output = $"RedirectStandardError : {errorOutput}. \n RedirectStandardOutput : {output}";
                    }
                    return output;
                }
            }
        }


        private async Task<string> FindExecutableDirectoryInPath(string commandName, string findCommand)
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = findCommand;
                process.StartInfo.Arguments = commandName;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string errorOutput = await process.StandardError.ReadToEndAsync();

                if (!string.IsNullOrWhiteSpace(errorOutput))
                {
                    _logger.LogError("Error finding executable: " + errorOutput);
                }

                await process.WaitForExitAsync();

                // Get the first path found by the 'where' command
                string exePath = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                if (!string.IsNullOrEmpty(exePath))
                {
                    // Return the directory part of the path
                    return Path.GetDirectoryName(exePath) ?? "";
                }

                return ""; // Return empty string if no path is found
            }
        }

        private void ProcessMetasploitOutput(string output)
        {
            // Process the output here if necessary, or log it
            _logger.LogInformation($"Metasploit output: {output}");
            _cmdProcessorStates.CompletedMessage += $"Metasploit output: {output}\n";
        }


    }
}
