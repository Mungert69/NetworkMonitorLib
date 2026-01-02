using System;
using System.Text;
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
using System.Xml.Linq;
using System.IO;
using System.Threading;
using NetworkMonitor.Service.Services.OpenAI;

namespace NetworkMonitor.Connection
{
    public class BusyboxCmdProcessor : CmdProcessor
    {

        public BusyboxCmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
: base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {

        }

        private string TrimShellCommand(string arguments)
        {
            // Remove "sh -c" or "sh" from the start if they exist
            if (arguments.TrimStart().StartsWith("sh -c"))
            {
                arguments = arguments.Substring(5).Trim();
            }
            else if (arguments.TrimStart().StartsWith("sh"))
            {
                arguments = arguments.Substring(2).Trim();
            }

            // Trim any leading or trailing whitespaces
            arguments = arguments.Trim();

            // Remove quotes from the beginning and end if they exist
            if (arguments.StartsWith("\"") && arguments.EndsWith("\""))
            {
                arguments = arguments.Substring(1, arguments.Length - 2).Trim();
            }
            if (arguments.StartsWith("'") && arguments.EndsWith("'"))
            {
                arguments = arguments.Substring(1, arguments.Length - 2).Trim();
            }

            return arguments;
        }

#if ANDROID
        private string BuildShellArgs(string arguments)
        {
            var trimmed = TrimShellCommand(arguments);
            var escaped = trimmed.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"-c \"{escaped}\"";
        }
#endif

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

#if ANDROID
                if (OperatingSystem.IsAndroid())
                {
                    string workingDirectory = _netConfig.CommandPath;
                    if (string.IsNullOrWhiteSpace(workingDirectory))
                    {
                        workingDirectory = Environment.CurrentDirectory;
                        _logger.LogWarning("Busybox working directory not set; falling back to {Dir}", workingDirectory);
                    }
                    string execPath;
                    string execArgs;
                    IPlatformProcessRunner runner;

                    if (arguments.TrimStart().StartsWith("sh", StringComparison.Ordinal))
                    {
                        runner = new AndroidProcWrapperRunner(_logger);
                        execPath = "/system/bin/sh";
                        execArgs = BuildShellArgs(arguments);
                    }
                    else
                    {
                        runner = new BusyboxProcessRunner(_logger);
                        execPath = "libbusybox_exec.so";
                        execArgs = arguments;
                    }

                    output = await runner.RunAsync(execPath, execArgs, workingDirectory, null, cancellationToken);
                    result.Success = !output.StartsWith("Failed to start:", StringComparison.OrdinalIgnoreCase);
                    result.Message = output;
                    return result;
                }
#endif

                using (var process = new Process())
                {
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    {
                        // Windows environment - Use cmd.exe
                        process.StartInfo.FileName = "cmd.exe";

                        // If the command starts with "sh" or "sh -c", run it as-is (as Windows doesn't natively support sh)
                        if (arguments.TrimStart().StartsWith("sh") || arguments.TrimStart().StartsWith("sh -c"))
                        {
                            process.StartInfo.Arguments = $"/C {TrimShellCommand(arguments)}";
                        }
                        else
                        {
                            // Otherwise, use busybox to run the command
                            process.StartInfo.Arguments = $"/C {_netConfig.CommandPath + _cmdProcessorStates.CmdName} {arguments}";
                        }
                    }
                    else if (Environment.OSVersion.Platform == PlatformID.Unix)
                    {
                        // Linux/Unix or Android environment
                        string shell = Environment.OSVersion.VersionString.Contains("Android") ? "/system/bin/sh" : "/bin/sh";

                        // Check if the command starts with "sh" or "sh -c"
                        if (arguments.TrimStart().StartsWith("sh") || arguments.TrimStart().StartsWith("sh -c"))
                        {

                            // Run the command directly using the shell
                            process.StartInfo.FileName = shell;
                            process.StartInfo.Arguments = $"-c \"{TrimShellCommand(arguments)}\"";
                        }
                        else
                        {
                            process.StartInfo.FileName = shell;
                            // For other commands, use busybox to run them
                            process.StartInfo.Arguments = $"-c \"{_netConfig.CommandPath + _cmdProcessorStates.CmdName} {arguments}\"";
                        }
                    }
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true; // Add this to capture standard error

                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.WorkingDirectory = _netConfig.CommandPath;

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
                            if (errorOutput.Contains("applet not found"))
                            {
                                output = "Busybox does not contain the command. Try running it directly by passing the arguments: \"sh <command>\".";
                            }
                            else
                            {
                                output = $"RedirectStandardError: {errorOutput}\nRedirectStandardOutput: {output}";
                            }
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
The BusyboxCmdProcessor leverages the BusyBox utility to execute a wide range of commands in Unix-like environments. 
BusyBox combines tiny versions of many common UNIX utilities into a single executable, making it versatile for system administration and scripting tasks.

### Usage:

**Basic Command**:
- Provide the command to execute as an argument. 
- If the command is not found in BusyBox, you can directly invoke it using a shell by prefixing the command with `sh`.

### Supported Commands:
BusyBox includes a vast array of commands, such as:

1. **File and Directory Management**:
   - `ls`: List directory contents.
     Example: `ls -l /home`
   - `mkdir`: Create a directory.
     Example: `mkdir new_directory`
   - `cp`: Copy files or directories.
     Example: `cp file1 file2`

2. **File Operations**:
   - `cat`: Display file contents.
     Example: `cat /etc/passwd`
   - `head`: Display the first lines of a file.
     Example: `head -n 10 log.txt`
   - `tail`: Display the last lines of a file.
     Example: `tail -f log.txt`

3. **Process Management**:
   - `ps`: Display information about active processes.
     Example: `ps -ef`
   - `kill`: Terminate a process by its ID.
     Example: `kill 1234`

4. **Network Tools**:
   - `ping`: Test connectivity to a host.
     Example: `ping 8.8.8.8`
   - `wget`: Download files from the web.
     Example: `wget http://example.com/file`
   - `netstat`: Display network connections.
     Example: `netstat -an`

5. **System Information**:
   - `uname`: Display system information.
     Example: `uname -a`
   - `df`: Show disk space usage.
     Example: `df -h`
   - `top`: Monitor system processes.
     Example: `top`

6. **Text Processing**:
   - `grep`: Search text using patterns.
     Example: `grep 'error' log.txt`
   - `sed`: Stream editor for filtering and transforming text.
     Example: `sed 's/old/new/g' file.txt`
   - `awk`: Pattern scanning and processing.
     Example: `awk '{print $1}' file.txt`

7. **Archiving and Compression**:
   - `tar`: Archive files.
     Example: `tar -czf archive.tar.gz directory`
   - `gzip`: Compress files.
     Example: `gzip file.txt`
   - `unzip`: Extract files from a zip archive.
     Example: `unzip file.zip`

8. **Shell Commands**:
   - `sh`: Invoke a shell for more complex commands.
     Example: `sh -c 'echo Hello, World!'`

### Features:

1. **Cross-Platform Support**:
   - Automatically handles differences between Windows and Unix-like systems.

2. **Shell Integration**:
   - If a command is unavailable in BusyBox, it can be run directly using the shell (`sh`).

3. **Error Handling**:
   - Captures and logs errors, providing detailed output for troubleshooting.

### Examples:

1. **List All Files in a Directory**:

ls -la /var/log


2. **Ping a Host**:

ping 192.168.1.1


3. **Download a File**:

wget http://example.com/sample.txt


4. **Search for a Pattern in a File**:

grep 'ERROR' /var/log/syslog


5. **Run a Custom Command via Shell**:

sh -c 'find /home -name ""*.log"" -print'


### Notes:

1. **Command Path**:
- Ensure BusyBox is correctly installed and the command path is configured in the agent settings.

2. **Unsupported Commands**:
- If a command is not part of BusyBox, the processor will attempt to run it using the shell.

3. **Command Output**:
- Standard output and error streams are captured and returned for review.

### Troubleshooting:

1. **Command Not Found**:
- Ensure the command is part of BusyBox or use `sh` for direct execution.

2. **Permission Denied**:
- Verify that the agent has the necessary permissions to execute the command.

3. **Incomplete Output**:
- Check if the command requires additional arguments or produces a large amount of data.

### Summary:
The BusyboxCmdProcessor is an essential tool for executing a variety of system commands efficiently. It provides robust error handling, shell integration, and comprehensive support for BusyBox utilities, making it a versatile choice for system administration tasks.
";
}

    }
}
