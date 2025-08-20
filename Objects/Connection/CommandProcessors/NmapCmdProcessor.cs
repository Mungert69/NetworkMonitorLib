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
using NetworkMonitor.Utils.Helpers;
using System.Xml.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NetworkMonitor.Service.Services.OpenAI;

namespace NetworkMonitor.Connection
{
    public class NmapCmdProcessor : CmdProcessor
    {

        public NmapCmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
: base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {

        }



        public override async Task Scan()
        {
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    _logger.LogWarning(" Warning : Nmap is not enabled or installed on this agent.");
                    var output = "The nmap command is not available on this agent. Try using another agent.\n";
                    _cmdProcessorStates.IsSuccess = false;
                    _cmdProcessorStates.IsRunning = false;
                    await SendMessage(output, null);
                    return;

                }


                _cmdProcessorStates.IsRunning = true;
                _cancellationTokenSource = new CancellationTokenSource();
                CancellationToken cancellationToken = _cancellationTokenSource.Token;


                var selectedInterface = _cmdProcessorStates.SelectedNetworkInterface;
                if (selectedInterface == null)
                {
                    throw new Exception("No network interface selected.");
                }

                var networkRange = $"{selectedInterface.IPAddress}/{selectedInterface.CIDR}";

                _logger.LogInformation($"Starting service scan on network range: {networkRange}");
                _cmdProcessorStates.RunningMessage += $"Starting service scan on network range: {networkRange}\n";

                var result = await RunCommand($" -sn {networkRange}", cancellationToken);
                var nmapOutput = result.Message;
                if (result.Success)
                {
                    var hosts = ParseNmapOutput(nmapOutput);

                    _logger.LogInformation($"Found {hosts.Count} hosts");
                    _cmdProcessorStates.RunningMessage += $"Found {hosts.Count} hosts\n";

                    foreach (var host in hosts)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); // Check for cancellation
                        await ScanHostServices(host, cancellationToken);
                    }
                    _cmdProcessorStates.CompletedMessage += "Service scan completed successfully.\n";

                    _cmdProcessorStates.IsSuccess = true;
                }
                else
                {
                    _cmdProcessorStates.CompletedMessage += $"Service scan falied {result.Message}.\n";

                    _cmdProcessorStates.IsSuccess = false;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Scan was cancelled.");
                _cmdProcessorStates.CompletedMessage += "Scan was cancelled.\n";
                _cmdProcessorStates.IsSuccess = false;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error during service scan: {e.Message}");
                _cmdProcessorStates.CompletedMessage += $"Error during service scan: {e.Message}\n";
                _cmdProcessorStates.IsSuccess = false;
            }
            finally
            {
                _cmdProcessorStates.IsRunning = false;
            }
        }

        public override async Task AddServices()
        {

            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    _logger.LogWarning(" Warning : Nmape is not enabled or installed on this agent.");
                    var output = "The scan command is not available on this agent. Try using another agent.\n";
                    _cmdProcessorStates.IsSuccess = false;
                    _cmdProcessorStates.IsRunning = false;
                    await SendMessage(output, null);
                    return;

                }
                var selectedDevices = _cmdProcessorStates.SelectedDevices.ToList();
                if (selectedDevices != null && selectedDevices.Count > 0)
                {
                    var processorDataObj = new ProcessorDataObj();
                    processorDataObj.AppID = _netConfig.AppID;
                    processorDataObj.AuthKey = _netConfig.AuthKey;
                    processorDataObj.RabbitPassword = _netConfig.LocalSystemUrl.RabbitPassword;
                    processorDataObj.MonitorIPs = selectedDevices;
                    await _rabbitRepo.PublishAsync<ProcessorDataObj>("saveMonitorIPs", processorDataObj);

                    _cmdProcessorStates.CompletedMessage += $"\nSent {selectedDevices.Count} host services to Quantum Network Monitor Service. Please wait 2 mins for hosts to become live. You can view the in the Host Data menu or visit {_frontendUrl}/dashboard and login using the same email address you registered your agent with.\n";
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error during add services: {e.Message}");
                _cmdProcessorStates.CompletedMessage += $"Error during add services: {e.Message}\n";
                _cmdProcessorStates.IsSuccess = false;
            }

        }


        public override async Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj();
            string output = "";
            try
            {
                if (!_cmdProcessorStates.IsCmdAvailable)
                {
                    _logger.LogWarning("Warning: Nmap is not enabled or installed on this agent.");
                    output = "Nmap is not available on this agent. Try installing the Quantum Secure Agent or select an agent that has Nmap enabled.\n";
                    result.Message = await SendMessage(output, processorScanDataObj);
                    result.Success = false;
                    return result;
                }

                string xmlOutput = processorScanDataObj == null ? " -oX -" : "";
                string extraArg = "";
                extraArg = " --system-dns ";
                string exePath = _netConfig.CommandPath;
                string dataDir = "";

                if (_netConfig.NativeLibDir != string.Empty)
                {
                    exePath = _netConfig.NativeLibDir;
                    dataDir = " --datadir " + _netConfig.CommandPath;
                    LibraryHelper.SetLDLibraryPath(_netConfig.NativeLibDir);
                }
                string nmapPath = Path.Combine(exePath, "nmap");
                using var process = new Process();
                process.StartInfo.FileName = nmapPath;
                process.StartInfo.Arguments = arguments + xmlOutput + extraArg + dataDir;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
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

                using (cancellationToken.Register(() =>
                {
                    if (!process.HasExited)
                    {
                        _logger.LogInformation("Cancellation requested, killing the Nmap process...");
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error killing process: {ex.Message}");
                        }
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
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Nmap command was cancelled.");
                output += "Nmap command was cancelled.\n";
                result.Success = false;
            }
            catch (Exception e)
            {
                _logger.LogError($"Error running Nmap command: {e.Message}");
                output += $"Error running Nmap command: {e.Message}\n";
                result.Success = false;
            }

            result.Message = output;
            return result;
        }

        public override string GetCommandHelp()
        {
            return @"
The Nmap Command Processor integrates Nmap into the agent, so you only need to provide the arguments to customize its behavior. 
This processor fully supports Nmap's extensive features, including host discovery, service detection, and vulnerability scanning. 
The arguments you provide will directly modify how Nmap executes.

### Usage:

**Basic Command**:
- Specify valid Nmap arguments to control the scan.
- The Nmap executable is already configured; only provide arguments for customization.

### Features and Examples:

1. **Host Discovery**:
   Identify active hosts on a network.

arguments: -sn 192.168.1.0/24

This uses the `-sn` argument to scan the subnet `192.168.1.0/24` for live hosts without performing a port scan.

2. **Port Scanning**:
Detect open ports and identify the services running on them.

arguments: -p 80,443 192.168.1.1

This scans ports `80` and `443` on the host `192.168.1.1`.

3. **Service and Version Detection**:
Discover running services and their versions on open ports.

arguments: -sV 192.168.1.1


4. **Script Scanning**:
Use Nmap's scripting engine (NSE) to perform security checks or gather additional information.

arguments: --script vuln 192.168.1.1

Example: Check for Heartbleed vulnerability.

arguments: --script ssl-heartbleed 192.168.1.1


5. **Operating System Detection**:
Determine the target's operating system.

arguments: -O 192.168.1.1


6. **Comprehensive Port Scanning**:
Scan all 65,535 TCP ports for detailed analysis.

arguments: -p- 192.168.1.1


7. **Custom Output**:
Save results in XML format for further processing.

arguments: -oX output.xml 192.168.1.1


8. **Aggressive Scans**:
Combine advanced features like OS detection, version detection, and default scripts.

arguments: -A 192.168.1.1


9. **Network Range Scanning**:
Scan an entire subnet for hosts and services.

arguments: -sS 192.168.1.0/24


10. **Custom Script Execution**:
 Automate tasks with custom or predefined scripts.
 ```
 arguments: --script custom-script 192.168.1.1
 ```

### Notes:

- **Nmap Integration**: Nmap is already preconfigured in this processor. Simply pass the required arguments to modify its execution.
- **Script Categories**: Supported script categories include:
- `vuln`: Check for vulnerabilities.
- `auth`: Test authentication mechanisms.
- `default`: Run default scripts.
- `discovery`: Gather information about the target.

- **Additional Options**:
- `--top-ports <n>`: Scan the top `n` most common ports.
- `--version-light`: Perform a faster but less detailed version detection.

### Troubleshooting:

1. **Error: 'command not available'**:
- Ensure Nmap is installed on the agent and accessible via the configured path.

2. **Incomplete Results**:
- Use the `-p-` argument to scan all ports or specify specific ports to avoid default exclusions.

3. **Slow Scans**:
- Use `-F` (fast mode) or limit scanned ports with `--top-ports` to reduce scan time.

4. **Parsing Issues**:
- Use the `-oX` option for machine-readable XML output.

### Examples Summary:

- Discover active hosts: `arguments: -sn 192.168.1.0/24`
- Vulnerability scan: `arguments: --script vuln 192.168.1.1`
- Service detection: `arguments: -sV 192.168.1.1`
- Aggressive scan: `arguments: -A 192.168.1.1`
- Comprehensive scan: `arguments: -p- 192.168.1.1`

This processor simplifies running Nmap by handling the tool's setup internally, allowing you to focus solely on specifying the arguments for your desired scans.
";
        }
        private List<string> ParseNmapOutputOld(string output)
        {
            var hosts = new List<string>();
            var regex = new Regex(@"Nmap scan report for (.+)");
            var matches = regex.Matches(output);

            foreach (Match match in matches)
            {
                hosts.Add(match.Groups[1].Value);
            }

            return hosts;
        }
        private List<string> ParseNmapOutput(string output)
        {
            var hosts = new List<string>();
            var xdoc = XDocument.Parse(output);

            var hostElements = xdoc.Descendants("host");
            foreach (var hostElement in hostElements)
            {
                var addressElement = hostElement.Descendants("address").FirstOrDefault(a => a.Attribute("addrtype")?.Value == "ipv4");
                if (addressElement != null)
                {
                    var addrAttribute = addressElement.Attribute("addr");
                    if (addrAttribute != null)
                    {
                        hosts.Add(addrAttribute.Value);
                    }
                }
            }

            return hosts;
        }

        private async Task ScanHostServices(string host, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"Scanning services on host: {host}");
            _cmdProcessorStates.RunningMessage += $"Scanning services on host: {host}\n";
            string fastScanArg = "";
            string limitPortsArg = "";
            if (_cmdProcessorStates.UseFastScan) fastScanArg = " --version-light";
            if (_cmdProcessorStates.LimitPorts) limitPortsArg = " -F";
            var result = await RunCommand($"{limitPortsArg}{fastScanArg} -sV {host}", cancellationToken);

            var nmapOutput = result.Message;
            string message = "";
            if (result.Success)
            {
                var services = ParseNmapServiceOutput(nmapOutput, host);

                foreach (var service in services)
                {
                    _cmdProcessorStates.ActiveDevices.Add(service);
                    message = $"Added service: {service.Address} on port {service.Port} for host {host} using endpoint type {service.EndPointType}\n";
                    _cmdProcessorStates.CompletedMessage += message;
                    _logger.LogInformation(message);

                }
            }
            else
            {
                message = $" Error : Failed to add services. {result.Message}";
                _cmdProcessorStates.CompletedMessage += message;
                _logger.LogInformation(message);
            }
        }
        private List<MonitorIP> ParseNmapServiceOutput(string output, string host)
        {
            _logger.LogInformation($"nmap output was : {output}");
            var monitorIPs = new List<MonitorIP>();
            var xdoc = XDocument.Parse(output);

            var portElements = xdoc.Descendants("port");
            foreach (var portElement in portElements)
            {
                // Safely retrieve attributes with null checks
                int port = int.Parse(portElement.Attribute("portid")?.Value ?? "0");
                string protocol = portElement.Attribute("protocol")?.Value?.ToLower() ?? "unknown";

                var serviceElement = portElement.Element("service");
                string serviceName = serviceElement?.Attribute("name")?.Value.ToLower() ?? "unknown";
                string version = serviceElement?.Attribute("version")?.Value ?? "unknown";
                string endPointType;
                if (_cmdProcessorStates.UseDefaultEndpointType) endPointType = _cmdProcessorStates.DefaultEndpointType;
                else endPointType = DetermineEndPointType(serviceName, protocol);

                var monitorIP = new MonitorIP
                {
                    Address = host,
                    Port = (ushort)port,
                    EndPointType = endPointType,
                    AppID = _netConfig.AppID,
                    UserID = _netConfig.Owner,
                    Timeout = 5000,
                    AgentLocation = _netConfig.MonitorLocation,
                    DateAdded = DateTime.UtcNow,
                    Enabled = true,
                    Hidden = false,
                    MessageForUser = $" service={serviceName} version=({version}) protocol=({protocol})"
                };

                monitorIPs.Add(monitorIP);
            }

            return monitorIPs;
        }
        private List<MonitorIP> ParseNmapServiceOutputOld(string output, string host)
        {
            var monitorIPs = new List<MonitorIP>();
            var regex = new Regex(@"(\d+)/(\w+)\s+(\w+)\s+(.+)");
            var matches = regex.Matches(output);

            foreach (Match match in matches)
            {
                int port = int.Parse(match.Groups[1].Value);
                string protocol = match.Groups[2].Value.ToLower();
                string serviceName = match.Groups[3].Value.ToLower();
                string version = match.Groups[4].Value;

                string endPointType;
                if (_cmdProcessorStates.UseDefaultEndpointType) endPointType = _cmdProcessorStates.DefaultEndpointType;
                else endPointType = DetermineEndPointType(serviceName, protocol);

                var monitorIP = new MonitorIP
                {
                    Address = host,
                    Port = (ushort)port,
                    EndPointType = endPointType,
                    AppID = _netConfig.AppID,
                    UserID = _netConfig.Owner,
                    Timeout = 5000,
                    AgentLocation = _netConfig.MonitorLocation,
                    DateAdded = DateTime.UtcNow,
                    Enabled = true,
                    Hidden = false,
                    MessageForUser = $"{serviceName} ({version})"
                };

                monitorIPs.Add(monitorIP);
            }

            return monitorIPs;
        }

        private string DetermineEndPointType(string serviceName, string protocol)
        {
            switch (serviceName)
            {
                case "http":
                    return "http";
                case "https":
                    return "https";
                case "domain":
                    return "dns";
                case "smtp":
                    return "smtp";
                case "ssh":
                case "telnet":
                case "ftp":
                    return "rawconnect";
                default:
                    if (protocol == "tcp")
                        return "rawconnect";
                    else
                        return "icmp";
            }
        }

    }



}