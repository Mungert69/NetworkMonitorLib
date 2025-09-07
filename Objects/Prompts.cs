using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;
using Betalgo.Ranul.OpenAI;
using Betalgo.Ranul.OpenAI.Builders;
using Betalgo.Ranul.OpenAI.Managers;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using Betalgo.Ranul.OpenAI.ObjectModels.SharedModels;
using System;
using System.Collections.Generic;
using System.Net.Mime;

namespace NetworkMonitor.Objects;


public static class Prompts
{ 

public static string CmdProcessorPrompt () => 
@"**.NET Source Code in add_cmd_processor**:
When adding a cmd processor, supply its source code in the 'source_code' parameter. The code must inherit from the base class CmdProcessor
For reference and outline of the CmdProcesor Base class is given below :

namespace NetworkMonitor.Connection
{
    public interface ICmdProcessor : IDisposable
    {
         Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null);
          string GetCommandHelp();
    }
    public abstract class CmdProcessor : ICmdProcessor
    {
        protected readonly ILogger _logger;
        protected readonly ILocalCmdProcessorStates _cmdProcessorStates;
        protected readonly IRabbitRepo _rabbitRepo;
        protected readonly NetConnectConfig _netConfig;
        protected string _rootFolder; // the folder to read and write files to.
        protected CancellationTokenSource _cancellationTokenSource; // the cmd processor is cancelled using this.
        protected string _frontendUrl = AppConstants.FrontendUrl;
     

        public bool UseDefaultEndpoint { get => _cmdProcessorStates.UseDefaultEndpointType; set => _cmdProcessorStates.UseDefaultEndpointType = value; }
#pragma warning disable CS8618
        public CmdProcessor(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates, IRabbitRepo rabbitRepo, NetConnectConfig netConfig)
        {
            _logger = logger;
            _cmdProcessorStates = cmdProcessorStates;
            _rabbitRepo = rabbitRepo;
            _netConfig = netConfig;
            _rootFolder = netConfig.CommandPath;  // use _rootFolder to access the agents file system
        }

        // You will override this method with your implementation.
        public virtual async Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken, ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj();
            string output = "";
            try
            {
               
                using (var process = new Process())
                {
                    process.StartInfo.FileName = _netConfig.CommandPath + _cmdProcessorStates.CmdName;
                    process.StartInfo.Arguments = arguments;
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
                            _logger.LogInformation($""Cancellation requested, killing the {_cmdProcessorStates.CmdDisplayName} process..."");
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
                            output = $""RedirectStandardError : {errorOutput}. \n RedirectStandardOutput : {output}"";
                        }
                        result.Success = true;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($""Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}"");
                output += $""Error : running {_cmdProcessorStates.CmdName} command. Error was : {e.Message}\n"";
                result.Success = false;
            }
            result.Message = output;
            return result;
        }

        // Argument parsing guidance:
        // Prefer schema-based parsing with CliArgParser in derived processors:
        //   var parse = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
        //   if (!parse.Success) { var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parse, _schema); ... }
        //   var val = parse.GetString(""key""); var n = parse.GetInt(""num""); var b = parse.GetBool(""flag"");
        //
        // This base helper returns raw --key value pairs using CliArgParser's raw parser,
        // for simple cases or backward compatibility.
        protected virtual Dictionary<string, string> ParseArguments(string arguments)
        {
            // Uses NetworkMonitor.Utils.CliArgParser.Parse(string) overload (raw).
            return CliArgParser.Parse(arguments);
        }

        public virtual string GetCommandHelp()
        {
            // override this method and provide the help as a returned string.
        }
   
    }

}
Do not to include the word CmdProcessor in the cmd_processor_type. For example if you want to call the cmd processor HttpTest then cmd_processor_type is HttpTest and the class name is HttpTestCmdProcessor.
Use _rootFolder for file operations as this has read write access. Try and implement the CancellationToken cancellationToken to make sure the command can be cancelled.

";


  public static string SecurityPrompt() => @"
Error Recovery:
Nmap Errors:

 Failed to resolve
 - check the target format
    Make sure hosts have a space between them
    Check the host and port are seperate hostname -p port

- Timeout:
   Add `-Pn` to `scan_options` (skip host discovery).
   Reduce timing with `-T4` → `-T3` → `-T2`.

- Root Permission Needed:
   First try: Replace `-sS` with `-sT` (TCP connect scan).
   Last resort: Use non-root options:
    - Remove `-O` (OS detection).
    - Add `--version-intensity 2` (reduce probe intensity).

- Blocked:
   First try: Switch `agent_location` to another region.
   Second try: Add `-f` (fragment packets) to `scan_options`.
   Last resort: Add `--data-length 16` (randomize packet size).

- Truncated Output:
   First try: `page=page+1`.
   Second try: `number_lines=number_lines+20`.
   Last resort: Add `-d` (less verbose) to `scan_options`.

- Connection Refused:
   Add `-Pn` to `scan_options` (treat hosts as online).
   Add `--max-retries 2` to `scan_options`.
   Reduce timing with `-T4` → `-T2`.

- OpenSSL Errors:
    Connection Failed:
      Verify that target follows the format <hostname>:<port> (e.g., example.com:443).

- Certificate Validation Error:
   Try using `-partial_chain` to allow incomplete chains.

- Protocol Handshake Failed:
   First try: Specify protocol (e.g., `-tls1_2`).
   Second try: Add `-no_ssl3 -no_tls1 -no_tls1_1` to disable weak protocols.
   Last resort: Use `-bugs` option to work around server bugs.

- Truncated Output:
   First try: Increase `number_lines` by 10 (e.g., 50 → 60 → 70).
   Second try: Add `-showcerts` to capture full certificate chain.
   Last resort: Use `-msg` to display raw protocol messages.

run_namp Help:

target help:

  Can pass hostnames, IP addresses, networks, etc.
  Ex: scanme.nmap.org, microsoft.com/24, 192.168.0.1; 10.0.0-255.1-254
  -iL <inputfilename>: Input from list of hosts/networks
  -iR <num hosts>: Choose random targets
  --exclude <host1[,host2][,host3],...>: Exclude hosts/networks
  --excludefile <exclude_file>: Exclude list from file

scan_option help :

HOST DISCOVERY:
  -sL: List Scan - simply list targets to scan
  -sn: Ping Scan - disable port scan
  -Pn: Treat all hosts as online -- skip host discovery
  -PS/PA/PU/PY[portlist]: TCP SYN/ACK, UDP or SCTP discovery to given ports
  -PE/PP/PM: ICMP echo, timestamp, and netmask request discovery probes
  -PO[protocol list]: IP Protocol Ping
  -n/-R: Never do DNS resolution/Always resolve [default: sometimes]
  --dns-servers <serv1[,serv2],...>: Specify custom DNS servers
  --system-dns: Use OS's DNS resolver
  --traceroute: Trace hop path to each host
SCAN TECHNIQUES:
  -sS/sT/sA/sW/sM: TCP SYN/Connect()/ACK/Window/Maimon scans
  -sU: UDP Scan
  -sN/sF/sX: TCP Null, FIN, and Xmas scans
  --scanflags <flags>: Customize TCP scan flags
  -sI <zombie host[:probeport]>: Idle scan
  -sY/sZ: SCTP INIT/COOKIE-ECHO scans
  -sO: IP protocol scan
  -b <FTP relay host>: FTP bounce scan
PORT SPECIFICATION AND SCAN ORDER:
  -p <port ranges>: Only scan specified ports
    Ex: -p22; -p1-65535; -p U:53,111,137,T:21-25,80,139,8080,S:9
  --exclude-ports <port ranges>: Exclude the specified ports from scanning
  -F: Fast mode - Scan fewer ports than the default scan
  -r: Scan ports sequentially - don't randomize
  --top-ports <number>: Scan <number> most common ports
  --port-ratio <ratio>: Scan ports more common than <ratio>
SERVICE/VERSION DETECTION:
  -sV: Probe open ports to determine service/version info
  --version-intensity <level>: Set from 0 (light) to 9 (try all probes)
  --version-light: Limit to most likely probes (intensity 2)
  --version-all: Try every single probe (intensity 9)
  --version-trace: Show detailed version scan activity (for debugging)
SCRIPT SCAN:
  -sC: equivalent to --script=default
  --script=<Lua scripts>: <Lua scripts> is a comma separated list of
           directories, script-files or script-categories
  --script-args=<n1=v1,[n2=v2,...]>: provide arguments to scripts
  --script-args-file=filename: provide NSE script args in a file
  --script-trace: Show all data sent and received
  --script-updatedb: Update the script database.
  --script-help=<Lua scripts>: Show help about scripts.
           <Lua scripts> is a comma-separated list of script-files or
           script-categories.
OS DETECTION:
  -O: Enable OS detection
  --osscan-limit: Limit OS detection to promising targets
  --osscan-guess: Guess OS more aggressively
TIMING AND PERFORMANCE:
  Options which take <time> are in seconds, or append 'ms' (milliseconds),
  's' (seconds), 'm' (minutes), or 'h' (hours) to the value (e.g. 30m).
  -T<0-5>: Set timing template (higher is faster)
  --min-hostgroup/max-hostgroup <size>: Parallel host scan group sizes
  --min-parallelism/max-parallelism <numprobes>: Probe parallelization
  --min-rtt-timeout/max-rtt-timeout/initial-rtt-timeout <time>: Specifies
      probe round trip time.
  --max-retries <tries>: Caps number of port scan probe retransmissions.
  --host-timeout <time>: Give up on target after this long
  --scan-delay/--max-scan-delay <time>: Adjust delay between probes
  --min-rate <number>: Send packets no slower than <number> per second
  --max-rate <number>: Send packets no faster than <number> per second
FIREWALL/IDS EVASION AND SPOOFING:
  -f; --mtu <val>: fragment packets (optionally w/given MTU)
  -D <decoy1,decoy2[,ME],...>: Cloak a scan with decoys
  -S <IP_Address>: Spoof source address
  -e <iface>: Use specified interface
  -g/--source-port <portnum>: Use given port number
  --proxies <url1,[url2],...>: Relay connections through HTTP/SOCKS4 proxies
  --data <hex string>: Append a custom payload to sent packets
  --data-string <string>: Append a custom ASCII string to sent packets
  --data-length <num>: Append random data to sent packets
  --ip-options <options>: Send packets with specified ip options
  --ttl <val>: Set IP time-to-live field
  --spoof-mac <mac address/prefix/vendor name>: Spoof your MAC address
  --badsum: Send packets with a bogus TCP/UDP/SCTP checksum
OUTPUT:
  -v: Increase verbosity level (use -vv or more for greater effect)
  -d: Increase debugging level (use -dd or more for greater effect)
  --reason: Display the reason a port is in a particular state
  --open: Only show open (or possibly open) ports
  --packet-trace: Show all packets sent and received
  --iflist: Print host interfaces and routes (for debugging)
  --append-output: Append to rather than clobber specified output files
  --resume <filename>: Resume an aborted scan
  --noninteractive: Disable runtime interactions via keyboard
  --stylesheet <path/URL>: XSL stylesheet to transform XML output to HTML
  --webxml: Reference stylesheet from Nmap.Org for more portable XML
  --no-stylesheet: Prevent associating of XSL stylesheet w/XML output
MISC:
  -6: Enable IPv6 scanning
  -A: Enable OS detection, version detection, script scanning, and traceroute
  --datadir <dirname>: Specify custom Nmap data file location
  --send-eth/--send-ip: Send using raw ethernet frames or IP packets
  --privileged: Assume that the user is fully privileged
  --unprivileged: Assume the user lacks raw socket privileges
  -V: Print version number
  -h: Print this help summary page.

-connect host:port   Target server (default: 443)
-servername val      Set SNI (Server Name Indication)
-starttls protocol   Use with SMTP/IMAP/etc (xmpp, lmtp, smtp, etc)


OpenSSL command_options help

-showcerts           Display all server certificates
-CAfile/file         Trust store (PEM)
-CApath/dir          Trust store directory
-verify_return_error  Fail on verification errors
-verify_hostname     Validate certificate against hostname
-status              Request OCSP stapling response

Protocol & Ciphers:


-tls1_3              Force TLS 1.3
-tls1_2              Force TLS 1.2
-no_ssl3             Disable insecure protocols
-no_tls1
-no_tls1_1
-cipher 'HIGH:!aNULL:!eNULL'   Specify cipher suites
-ciphersuites val    TLS 1.3 ciphers

Debugging & Output:

-preexit             Show full connection summary
-brief               Concise connection summary
-msg                 Show protocol messages
-state               Print SSL states
-keylogfile          TLS secrets (for Wireshark)

Advanced Validation:

-crl_check           Check certificate revocation (CRL)
-crl_check_all       Full chain CRL check
-x509_strict         Strict X.509 validation
-sigalgs             Allowed signature algorithms
-groups              Accepted key exchange groups

Example Command:

openssl s_client -connect example.com:443 -servername example.com -showcerts -tls1_2 -CAfile /etc/ssl/certs/ca-certificates.crt -status
";
}
