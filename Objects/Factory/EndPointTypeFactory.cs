using Microsoft.Extensions.Logging;
using NetworkMonitor.Connection;
using NetworkMonitor.Api.Services;
namespace NetworkMonitor.Objects.Factory
{
    public class ThresholdValues
    {
        public int Excellent { get; set; }
        public int Good { get; set; }
        public int Fair { get; set; }

        public ThresholdValues(int excellent, int good, int fair)
        {
            Excellent = excellent;
            Good = good;
            Fair = fair;
        }
    }

    public class ResponseTimeThreshold
    {
        public ThresholdValues AllPorts { get; set; }
        public ThresholdValues SpecificPort { get; set; }

        public ResponseTimeThreshold(ThresholdValues allPorts, ThresholdValues specificPort)
        {
            AllPorts = allPorts;
            SpecificPort = specificPort;
        }

        public ThresholdValues GetThresholds(int port) => port == 0 ? AllPorts : SpecificPort;
    }
    public static class EndPointTypeFactory
    {

        private static readonly List<EndpointType> _endpointTypes = new List<EndpointType>
        {
            new EndpointType("icmp", "PingIcon", "ICMP (Simple Ping)", "Simple ICMP Ping", "host ping"),
            new EndpointType("http", "HttpIcon", "Http (Website Ping)", "Ping a website via HTTP", "website ping"),
            new EndpointType("https", "HttpsIcon", "HttpSSL (SSL Certificate Check)", "Check SSL certificates via HTTPS", "SSL certificate check"),
            new EndpointType("httphtml", "HtmlIcon", "HttpHtml (Load Website HTML)", "Load website HTML content, no javascript", "loads only the HTML of a website"),
            new EndpointType("httpfull", "LanguageIcon", "HttpFull (Load All Website Content)", "Load full website content inc javascript", "loads full website content including JavaScript"),
            // New endpoint type for sitehash
            new EndpointType(
                "sitehash",
                "HashIcon",
                "SiteHash (Website Content Hash Check)",
                "Load full website content using Puppeteer, hash the rendered HTML, and compare to a stored value for change detection.",
                "loads and hashes rendered website content to detect changes"
            ),
            new EndpointType("dns", "DnsIcon", "DNS (Domain Lookup)", "Perform DNS lookups", "DNS lookup"),
            new EndpointType("smtp", "EmailIcon", "SMTP (Email Ping)", "Ping email via SMTP", "email server HELO message confirmation"),
            new EndpointType("quantum", "QuantumIcon", "Quantum (Quantum Ready Check)", "Quantum readiness checks", "a quantum-safe encryption test"),
            new EndpointType("quantumcert", "QuantumIcon", "Quantum Cert (Certificate PQC Check)", "Quantum certificate checks", "a quantum-safe certificate test"),
            new EndpointType("rawconnect", "LinkIcon", "Raw Connect (Socket Connection)", "Establish raw socket connections", "low-level raw socket connection"),
            new EndpointType(
                "blebroadcast",
                "BluetoothIcon",
                "BLE Broadcast",
                "Listen for BLE broadcasts from a specific address and optionally decrypt payloads. Uses Password as the key. Extra flags go in Args, e.g. --format raw|aesgcm|aesctr|victron --nonce_len 12 --tag_len 16 --nonce_at start --payload manufacturer|service|raw --manufacturer_id <int> --service_uuid <uuid> --raw_payload <hex> --metric pv_power|battery_voltage|battery_current|yield_today|load_current. Example: address=CE:65:1B:7B:C0:C8 password=<key> args=\"--format victron --metric pv_power\".",
                "BLE broadcast (targeted). Password=key, Args=cmd flags"
            ),
            new EndpointType(
                "blebroadcastlisten",
                "BluetoothIcon",
                "BLE Broadcast Listen",
                "Listen for any BLE broadcasts within the timeout window and return a capped list of payloads. Password is optional (used as the key). Extra flags go in Args, e.g. --format raw|aesgcm|aesctr|victron --nonce_len 12 --tag_len 16 --nonce_at start --payload manufacturer|service|raw --manufacturer_id <int> --service_uuid <uuid> --max_captures <int>.",
                "BLE broadcast (listen mode, no address required)"
            ),
            new EndpointType("nmap", "NmapIcon", "NmapScan (Service Scan)", "Perform Nmap service scans", "service scan using Nmap"),
            new EndpointType("nmapvuln", "NmapVulnIcon", "NmapVuln (Vulnerability Scan)", "Perform Nmap vulnerability scans", "vulnerability scan using Nmap scripts"),
            new EndpointType("crawlsite", "CrawlSiteIcon", "CrawlSite (Traffic Generator)", "Generate traffic by crawling sites", "traffic generator that crawls a site"),
            new EndpointType("dailycrawl", "CrawlSiteIcon", "Daily CrawlSite {Low Traffic Generator}", "Generate once daily traffic by crawling sites", "once-daily low-traffic site crawl"),
            new EndpointType("dailyhugkeepalive", "HugIcon", "Daily HuggingFace Keep Alive {Traffic Generator}", "Generate once daily traffic to keep alive  a huggingface space", "once-daily traffic generator for a Hugging Face space to keep it alive"),
            new EndpointType("hugwake", "HugIcon", "Hourly HuggingFace Wake Up {Click Restart}", "Searches for and clicks restart on a huggingface space", "wake up a Hugging Face space by clicking the restart button if the space is sleeping")


        };

        public static string GetProcessingTimeEstimate(string? endpointType)
        {
            endpointType = endpointType?.ToLower() ?? "";
            if (endpointType.Contains("daily"))
                return "One day (daily only runs once a day)";
            if (endpointType.Contains("nmap"))
                return "15-30 minutes (comprehensive network scans take longer)";
            if (endpointType.Contains("crawlsite"))
                return "30-60 minutes (website crawling is resource intensive)";
            if (endpointType.Contains("smtp") || endpointType.Contains("quantum"))
                return "5-10 minutes";
            if (endpointType.Contains("http") || endpointType.Contains("https"))
                return "2-5 minutes";
            if (endpointType.Contains("ping"))
                return "1-2 minutes";
            if (endpointType.Contains("ble"))
                return "5-20 seconds (depends on broadcast interval)";

            return "2-10 minutes";
        }
        /// <summary>
        ///  Dictionary of values for the response time thresholds that are considered to be either excellent , good or fair. there are two sets because the port zero can do more work for some endpoint types (nmap).
        /// </summary>
        public static readonly Dictionary<string, ResponseTimeThreshold> ResponseTimeThresholds = new()
{
    { "icmp", new ResponseTimeThreshold(new ThresholdValues(50, 100, 200), new ThresholdValues(50, 100, 200)) },
    
    // HTTP (header only) - faster thresholds as it's just header retrieval
    { "http", new ResponseTimeThreshold(new ThresholdValues(150, 300, 500), new ThresholdValues(150, 300, 500)) },
    
    // HTTPHTML (text-only load) - slightly slower than header only, but still fast
    { "httphtml", new ResponseTimeThreshold(new ThresholdValues(250, 500, 800), new ThresholdValues(250, 500, 800)) },
    
    // HTTPFULL (full page load with Puppeteer) - significantly higher thresholds for full content load
    { "httpfull", new ResponseTimeThreshold(new ThresholdValues(2000, 4000, 8000), new ThresholdValues(2000, 4000, 8000)) },

    // SiteHash (full page load with Puppeteer and hash check) - same thresholds as httpfull
    { "sitehash", new ResponseTimeThreshold(new ThresholdValues(2000, 4000, 8000), new ThresholdValues(2000, 4000, 8000)) },
    
    // DNS - DNS lookups are generally quick, with moderate thresholds
    { "dns", new ResponseTimeThreshold(new ThresholdValues(100, 300, 600), new ThresholdValues(100, 300, 600)) },
    
    // SMTP - EHLO response should be fairly quick
    { "smtp", new ResponseTimeThreshold(new ThresholdValues(200, 400, 700), new ThresholdValues(200, 400, 700)) },
    
    // Quantum - TLS handshake with OQSProvider may have slight latency, but existing values are reasonable
    { "quantum", new ResponseTimeThreshold(new ThresholdValues(800, 1500, 3000), new ThresholdValues(800, 1500, 3000)) },
    { "quantumcert", new ResponseTimeThreshold(new ThresholdValues(800, 1500, 3000), new ThresholdValues(800, 1500, 3000)) },
    
    // RawConnect - Raw socket connection is fast, so keeping low thresholds
    { "rawconnect", new ResponseTimeThreshold(new ThresholdValues(100, 200, 400), new ThresholdValues(100, 200, 400)) },
    { "blebroadcast", new ResponseTimeThreshold(new ThresholdValues(500, 1000, 2000), new ThresholdValues(500, 1000, 2000)) },
    { "blebroadcastlisten", new ResponseTimeThreshold(new ThresholdValues(500, 1000, 2000), new ThresholdValues(500, 1000, 2000)) },

    // Adjusted thresholds for nmap scans based on observed execution times. Note these are 10 times less than the above as the timeout is set is 10s of milliseconds nmap connects.
    { "nmap", new ResponseTimeThreshold(new ThresholdValues(0, 0, 0), new ThresholdValues(0, 0, 0)) },
    { "nmapvuln", new ResponseTimeThreshold(new ThresholdValues(0, 0, 0), new ThresholdValues(0, 0, 0)) },
    { "crawlsite", new ResponseTimeThreshold(new ThresholdValues(0, 0, 0), new ThresholdValues(0, 0, 0)) },
    {"dailycrawl", new ResponseTimeThreshold(new ThresholdValues(0, 0, 0), new ThresholdValues(0, 0, 0)) },
    {"dailyhugkeepalive", new ResponseTimeThreshold(new ThresholdValues(0, 0, 0), new ThresholdValues(0, 0, 0)) },
    {"hugwake", new ResponseTimeThreshold(new ThresholdValues(0, 0, 0), new ThresholdValues(0, 0, 0)) }

};



        public static List<EndpointType> GetEndpointTypes()
        {
            return _endpointTypes;
        }

        public static List<string> GetInternalTypes()
        {
            return _endpointTypes.Select(et => et.InternalType).ToList();
        }

        public static List<string> GetFriendlyNames()
        {
            return _endpointTypes.Select(et => et.Name).ToList();
        }

        public static string GetFriendlyName(string internalType)
        {
            var endpoint = _endpointTypes.FirstOrDefault(et => et.InternalType.Equals(internalType, StringComparison.OrdinalIgnoreCase));
            return endpoint?.Name ?? "Unknown";
        }

        public static string GetInternalType(string friendlyName)
        {
            var endpoint = _endpointTypes.FirstOrDefault(et => et.Name.Equals(friendlyName, StringComparison.OrdinalIgnoreCase));
            return endpoint?.InternalType ?? throw new ArgumentException("Invalid friendly name provided.");
        }

        public static EndpointType GetEndpointType(string internalType)
        {
            return _endpointTypes.FirstOrDefault(et => et.InternalType.Equals(internalType, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException("Invalid internal type provided.");
        }

        public static EndpointType GetEndpointTypeByName(string friendlyName)
        {
            return _endpointTypes.FirstOrDefault(et => et.Name.Equals(friendlyName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException("Invalid friendly name provided.");
        }

        public static List<string> GetEnabledEndPoints(List<string> disabledEndPointTypes)
        {
            // Get the list of all internal types
            var allInternalTypes = GetInternalTypes();

            // Filter out the disabled internal types
            var enabledEndpoints = allInternalTypes
                .Where(type => !disabledEndPointTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return enabledEndpoints;
        }


        // Method to create the correct INetConnect instance based on type
        public static INetConnect CreateNetConnect(string type, HttpClient httpClient, HttpClient httpsClient, List<AlgorithmInfo> algorithmInfoList, string oqsProviderPath, string commandPath, ILogger logger, ICmdProcessorProvider? cmdProcessorProvider = null, IBrowserHost? browserHost = null, string nativeLibDir = "")
        {
            return type switch
            {
                "http" => new HTTPConnect(httpClient, false, false, commandPath),
                "https" => new HTTPConnect(httpsClient, false, false, commandPath),
                "httphtml" => new HTTPConnect(httpClient, true, false, commandPath),
                "httpfull" => new HTTPConnect(httpClient, false, true, commandPath, browserHost),
                "sitehash" => new SiteHashConnect(commandPath, browserHost), // New endpoint type
                "dns" => new DNSConnect(),
                "smtp" => new SMTPConnect(),
                "quantum" => new QuantumConnect(algorithmInfoList, oqsProviderPath, commandPath, logger, nativeLibDir),
                "quantumcert" => new QuantumCertConnect(oqsProviderPath, commandPath, nativeLibDir, logger),
                "rawconnect" => new SocketConnect(),
                "blebroadcast" => new BleBroadcastConnect(cmdProcessorProvider),
                "blebroadcastlisten" => new BleBroadcastListenConnect(cmdProcessorProvider),
                "nmap" => new NmapCmdConnect(cmdProcessorProvider, "-sV"),
                "nmapvuln" => new NmapCmdConnect(cmdProcessorProvider, "--script vuln"),
                "crawlsite" => new CrawlSiteCmdConnect(cmdProcessorProvider, " --max_depth 3 --max_pages 10"),
                "dailycrawl" => new CrawlSiteCmdConnect(cmdProcessorProvider, " --max_depth 4 --max_pages 20"),
                "dailyhugkeepalive" => new HugSpaceKeepAliveConnect(cmdProcessorProvider, ""),
                "hugwake" => new HugSpaceWakeConnect(cmdProcessorProvider, ""),
                _ => new ICMPConnect(),
            };
        }
        public static async Task<TResultObj<DataObj>> TestConnection(
    string type,
    IApiService apiService,
    HostObject hostObject,
    string address,
    ushort port)
        {
            type = type.ToLower();  // Convert type to lowercase for case-insensitive comparison

            if (type.Contains("http"))
            {
                return await apiService.CheckHttp(hostObject);
            }
            else if (type.Contains("https"))
            {
                return await apiService.CheckHttps(hostObject);
            }
            else if (type.Contains("smtp"))
            {
                return await apiService.CheckSmtp(hostObject);
            }
            else if (type.Contains("dns"))
            {
                return await apiService.CheckDns(hostObject);
            }
            else if (type.Contains("icmp"))
            {
                return await apiService.CheckIcmp(hostObject);
            }
            else if (type.Contains("rawconnect"))
            {
                return await apiService.CheckRawconnect(hostObject);
            }
            else if (type.Contains("nmap"))
            {
                return await apiService.CheckNmap(hostObject);
            }
            else if (type.Contains("crawlsite"))
            {
                return await apiService.CheckCrawlSite(hostObject);
            }
            else if (type.Contains("dailycrawl"))
            {
                return await apiService.CheckCrawlSite(hostObject);
            }
            else if (type.Contains("blebroadcast"))
            {
                return new TResultObj<DataObj>
                {
                    Success = false,
                    Message = "BLE broadcast checks are only supported on Android agents.",
                    Data = new DataObj
                    {
                        ResultStatus = "BLE broadcast checks are agent-only.",
                        ResponseTime = -1
                    }
                };
            }
            else if (type.Contains("quantumcert"))
            {
                return await apiService.CheckQuantumCert(new HostObject
                {
                    Address = address,
                    Port = port,
                    EndPointType = "quantumcert"
                });
            }
            else if (type.Contains("quantum"))
            {
                TResultObj<QuantumDataObj> quantumResult = await apiService.CheckQuantum(new QuantumHostObject { Address = address, Port = port });
                return new TResultObj<DataObj>
                {
                    Success = quantumResult.Success,
                    Message = quantumResult.Message,
                    Data = new DataObj
                    {
                        ResponseTime = quantumResult?.Data?.ResponseTime ?? UInt16.MaxValue,
                        ResultStatus = quantumResult?.Data?.ResultStatus ?? ""
                    }
                };
            }
            else
            {
                throw new ArgumentException("Invalid endpoint type selected.");
            }
        }

    }
}
