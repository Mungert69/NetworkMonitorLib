using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;
using System;
using System.Text.RegularExpressions;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using System.Diagnostics;

namespace NetworkMonitor.Connection
{
    public class NmapCmdConnect : NetConnect
    {
        private ICmdProcessor? _cmdProcessor;
        private string _baseArg;

        public NmapCmdConnect(ICmdProcessorProvider? cmdProcessorProvider, string baseArg)
        {
            if (cmdProcessorProvider != null) _cmdProcessor = cmdProcessorProvider.GetProcessor("Nmap");
            _baseArg = baseArg;
            IsLongRunning = true;
        }

        public override async Task Connect()
        {
            ExtendTimeout = true;

            if (_cmdProcessor == null)
            {
                ProcessException("No Command Processor Available", "Error");
                return;
            }

            PreConnect();
            var result = new ResultObj();
            ushort responseTime = 0;
            try
            {
                string address = MpiStatic.Address.StripHttpProtocol();

                ushort port = MpiStatic.Port;
                string arguments = $"{_baseArg} --system-dns {address}";
                if (MpiStatic.Port != 0)
                {
                    arguments = $"{_baseArg} --system-dns -p {port} {address}";
                }

                Timer.Reset();
                Timer.Start();
                var processorScanDataObj = new ProcessorScanDataObj
                {
                    Arguments = arguments,
                    SendMessage = false
                };
                result = await _cmdProcessor.QueueCommand(Cts, processorScanDataObj);
                Timer.Stop();

                string filteredString = ExtractNmapOutput(result.Message);

                bool isUp = true;
                var (isHostUp, hostStatus) = GetHostStatus(filteredString);
                string statusMessage = hostStatus;
                responseTime = (ushort)Timer.ElapsedMilliseconds;
                // Set response time to max value if host is down
                if (!isHostUp)
                {
                    responseTime = ushort.MaxValue;
                    isUp = false;
                }

                // Step 2: If host is up and command is a vulnerability scan, check for vulnerabilities
                if (isHostUp && _baseArg.Contains("--script vuln"))
                {
                    var (vulnFound, vulnStatus) = GetVulnerabilityStatus(filteredString);
                    statusMessage += "; " + vulnStatus;

                    // Set response time to max value if vulnerabilities are found
                    if (vulnFound)
                    {
                        responseTime = ushort.MaxValue;
                        isUp = false;
                    }
                }

                if (isUp) ProcessStatus(statusMessage, responseTime, filteredString);
                else ProcessException(filteredString, statusMessage);
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

        private string ExtractNmapOutput(string input)
        {
            // First stage: Try to remove the initial part up to "Nmap scan report..."
            string initialPattern = @"Nmap scan report for .+?\)(.*)";
            Match initialMatch = Regex.Match(input, initialPattern, RegexOptions.Singleline);

            // Check if initial match succeeded
            string output = initialMatch.Success ? initialMatch.Groups[1].Value.Trim() : input;

            // Second stage: Try to remove "Please report any incorrect results at https://nmap.org/submit/ ."
            string reportPattern = @"(.*?)(?:Please report any incorrect results at https://nmap\.org/submit/ \. )(.*)";
            Match reportMatch = Regex.Match(output, reportPattern, RegexOptions.Singleline);

            // If the report pattern matches, concatenate the parts before and after "Please report..."
            if (reportMatch.Success)
            {
                output = reportMatch.Groups[1].Value.Trim() + " " + reportMatch.Groups[2].Value.Trim();
            }

            return output;
        }


        private (bool, string) GetHostStatus(string input)
        {
            string upPattern = @"Host is up";
            string downPattern = @"Host seems down|0 hosts up";

            if (Regex.IsMatch(input, upPattern, RegexOptions.IgnoreCase))
            {
                return (true, "Port/s open");
            }
            else if (Regex.IsMatch(input, downPattern, RegexOptions.IgnoreCase))
            {
                return (false, "Port/s closed");
            }
            else
            {
                return (false, "Host status unknown");
            }
        }

        private (bool, string) GetVulnerabilityStatus(string input)
        {
            string positivePattern = @"\bVULNERABLE\b|Risk factor: High|CVE:\s*[A-Z0-9-]+";
            string negativePattern = @"Couldn't find|NOT VULNERABLE";

            foreach (string line in input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                if (Regex.IsMatch(line, negativePattern, RegexOptions.IgnoreCase))
                {
                    continue; // Skip lines that indicate no vulnerabilities
                }

                if (Regex.IsMatch(line, positivePattern, RegexOptions.IgnoreCase))
                {
                    return (true, "vulnerabilities found");
                }
            }
            return (false, "no vulnerabilities detected");
        }
    }
}
