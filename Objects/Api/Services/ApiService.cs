using Microsoft.Extensions.Logging;
using NetworkMonitor.Connection;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects;
using Microsoft.Extensions.Configuration;

namespace NetworkMonitor.Api.Services
{
    public interface IApiService
    {
        Task<TResultObj<QuantumDataObj>> CheckQuantum(QuantumHostObject host);
        Task<TResultObj<DataObj>> CheckSmtp(HostObject host);
        Task<TResultObj<DataObj>> CheckHttp(HostObject host);
        Task<TResultObj<DataObj>> CheckHttps(HostObject host);
        Task<TResultObj<DataObj>> CheckHttpHtml(HostObject host);
        Task<TResultObj<DataObj>> CheckHttpFull(HostObject host);
        Task<TResultObj<DataObj>> CheckSiteHash(HostObject host);
        Task<TResultObj<DataObj>> CheckDns(HostObject host);
        Task<TResultObj<DataObj>> CheckIcmp(HostObject host);
        Task<TResultObj<DataObj>> CheckNmap(HostObject host);
        Task<TResultObj<DataObj>> CheckNmapVuln(HostObject host);
        Task<TResultObj<DataObj>> CheckRawconnect(HostObject host);
        Task<TResultObj<DataObj>> CheckCrawlSite(HostObject host);
        Task<TResultObj<DataObj>> CheckDailyCrawl(HostObject host);
        Task<TResultObj<DataObj>> CheckDailyHugKeepAlive(HostObject host);

        Task<List<TResultObj<DataObj>>> CheckConnections(List<IConnectionObject> connectionObjects);

        List<string> RapidApiKeys { get; set; }
    }

    public class ApiService : IApiService
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly PingParams _pingParams;
        private readonly NetConnectCollection _netConnectCollection;
        private ISystemParamsHelper _systemParamsHelper;
              private List<string> _rapidApiKey;
        private ICmdProcessorProvider _cmdProcessorProvider;
        public List<string> RapidApiKeys { get => _rapidApiKey; set => _rapidApiKey = value; }

#pragma warning disable CS8618
        public ApiService(ILoggerFactory loggerFactory, IConfiguration config, ICmdProcessorProvider cmdProcessorProvider, string appDirectory = "", string nativeLibDir = "", IBrowserHost? browserHost = null)
        {
            try
            {
                _logger = loggerFactory.CreateLogger<ApiService>();
                _cmdProcessorProvider = cmdProcessorProvider;
                _config = config;
                _systemParamsHelper = new SystemParamsHelper(_config, loggerFactory.CreateLogger<SystemParamsHelper>());
                if (_systemParamsHelper == null) throw new ArgumentNullException(" Error _systemParamsHelper is null");
                if (_systemParamsHelper.GetSystemParams() == null) throw new ArgumentNullException(" Error _systemParamsHelper.GetSystemParams() is null");

                RapidApiKeys = _systemParamsHelper.GetSystemParams().RapidApiKeys;
                if (RapidApiKeys == null || RapidApiKeys.Count==0    )
                {
                    throw new ArgumentException(" Fatal error could not load RapidApiKey from appsettings.json");
                }

                _pingParams = _systemParamsHelper.GetPingParams();
                _logger.LogInformation(" Info : Set PingParams.");

                var netConnectConfig = new NetConnectConfig(_config, appDirectory, nativeLibDir);

                var connectFactory = new ConnectFactory(loggerFactory.CreateLogger<ConnectFactory>(), netConfig: netConnectConfig, cmdProcessorProvider: cmdProcessorProvider, browserHost: browserHost);
                _ = connectFactory.SetupChromium(netConnectConfig);
                _logger.LogInformation(" Info : ConnectFactory created.");
                _netConnectCollection = new NetConnectCollection(loggerFactory.CreateLogger<NetConnectCollection>(), netConnectConfig, connectFactory);
                if (_netConnectCollection != null)
                {
                    _netConnectCollection.SetPingParams(_pingParams);
                    _logger.LogInformation(" Info : NetConnectCollection created.");
                }
                else
                {
                    throw new ArgumentException(" Failed to create NetConnectCollection setup of it returned null");
                }
            }
            catch (Exception ex)
            {
                if (_logger != null) _logger.LogError(" Error : " + ex.Message);
            }
        }
#pragma warning restore CS8618

        public async Task<List<TResultObj<DataObj>>> CheckConnections(List<IConnectionObject> connectionObjects)
        {
            var results = new List<TResultObj<DataObj>>();

            foreach (var obj in connectionObjects)
            {
                if (obj is QuantumHostObject quantumHostObject)
                {
                    var quantumResult = await CheckQuantum(quantumHostObject) ?? new TResultObj<QuantumDataObj>();
                    results.Add(new TResultObj<DataObj>
                    {
                        Success = quantumResult.Success,
                        Message = quantumResult.Message,
                        Data = new DataObj
                        {
                            TestedAddress = quantumHostObject.Address,
                            TestedPort = quantumHostObject.Port,
                            ResultSuccess = quantumResult?.Data?.ResultSuccess ?? false,
                            ResponseTime = quantumResult?.Data?.ResponseTime ?? UInt16.MaxValue,
                            Timeout = quantumResult?.Data?.Timeout ?? 0,
                            ResultStatus = quantumResult?.Data?.ResultStatus ?? "",
                            CheckPerformed = "Quantum TLS Encryption"
                        }
                    });
                }
                else if (obj is HostObject hostObj)
                {
                    var result = new TResultObj<DataObj>();

                    string endPointType = (hostObj.EndPointType ?? string.Empty).ToLowerInvariant();

                    switch (endPointType)
                    {
                        case "http":
                            result = await CheckHttp(hostObj);
                            break;
                        case "httphtml":
                            result = await CheckHttpHtml(hostObj);
                            break;
                        case "httpfull":
                            result = await CheckHttpFull(hostObj);
                            break;
                        case "sitehash":
                            result = await CheckSiteHash(hostObj);
                            break;
                        case "https":
                            result = await CheckHttps(hostObj);
                            break;
                        case "smtp":
                            result = await CheckSmtp(hostObj);
                            break;
                        case "dns":
                            result = await CheckDns(hostObj);
                            break;
                        case "nmap":
                            result = await CheckNmap(hostObj);
                            break;
                        case "nmapvuln":
                            result = await CheckNmapVuln(hostObj);
                            break;
                        case "icmp":
                            result = await CheckIcmp(hostObj);
                            break;
                        case "rawconnect":
                            result = await CheckRawconnect(hostObj);
                            break;
                        case "crawlsite":
                            result = await CheckCrawlSite(hostObj);
                            break;
                        case "dailycrawl":
                            result = await CheckDailyCrawl(hostObj);
                            break;
                        case "dailyhugkeepalive":
                            result = await CheckDailyHugKeepAlive(hostObj);
                            break;
                        default:
                            if (endPointType.Contains("http"))
                            {
                                result = await CheckHttp(hostObj);
                            }
                            else
                            {
                                _logger.LogWarning($"Unsupported endpoint type: {hostObj.EndPointType}");
                            }
                            break;
                    }

                    if (result != null)
                    {
                        results.Add(result);
                    }
                }
                else
                {
                    _logger.LogWarning($"Unsupported object type: {obj.GetType().Name}");
                }
            }

            return results;
        }

        // Common method to perform checks
        private async Task<TResultObj<DataObj>> PerformCheck(
            HostObject hostObj,
            string serviceName,
            string checkPerformed,
            ushort defaultPort = 0,
            ushort defaultTimeout = 20000,
            Func<INetConnect, string>? customResultStatusHandler = null)
        {
            var result = new TResultObj<DataObj>();

            if (defaultPort > 0 && hostObj.Port == 0)
                hostObj.Port = defaultPort;
            if (defaultTimeout > 0 && hostObj.Timeout == 0)
                hostObj.Timeout = defaultTimeout;

            result.Message = $" SERVICE : {serviceName} : ";
            try
            {
                var monitorPingInfo = new MonitorPingInfo()
                {
                    Address = hostObj.Address,
                    Port = hostObj.Port,
                    EndPointType = hostObj.EndPointType,
                    Timeout = hostObj.Timeout,
                };

                var netConnect = _netConnectCollection.GetNetConnectInstance(monitorPingInfo);
                await netConnect.Connect();
                result.Message += netConnect.MpiConnect.PingInfo.Status;
                result.Success = netConnect.MpiConnect.IsUp;
                var data = new DataObj();
                data.TestedAddress = netConnect.MpiStatic.Address;
                data.TestedPort = netConnect.MpiStatic.Port;
                if (netConnect.MpiConnect.PingInfo.RoundTripTime != UInt16.MaxValue)
                    data.ResponseTime = netConnect.MpiConnect.PingInfo.RoundTripTime;
                else
                    data.Timeout = netConnect.MpiStatic.Timeout;
                data.ResultSuccess = netConnect.MpiConnect.IsUp;

                if (customResultStatusHandler != null)
                {
                    data.ResultStatus = customResultStatusHandler(netConnect);
                }
                else
                {
                    string[] splitData = result.Message.Split(new char[] { ':' }, 3);
                    if (splitData.Length > 2)
                    {
                        data.ResultStatus = splitData[2];
                    }
                }

                data.CheckPerformed = checkPerformed;
                result.Data = data;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += " Error : " + ex.Message;
                _logger.LogError(result.Message);
            }
            return result;
        }

        public async Task<TResultObj<DataObj>> CheckHttp(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckHttp", "Http Connection");
        }

        public async Task<TResultObj<DataObj>> CheckHttps(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckHttps", "SSL Certificate");
        }

        public async Task<TResultObj<DataObj>> CheckHttpHtml(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckHttpHtml", "Http HTML Content", defaultTimeout: 30000);
        }

        public async Task<TResultObj<DataObj>> CheckHttpFull(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckHttpFull", "Http Full Content", defaultTimeout: ushort.MaxValue - 1);
        }

        public async Task<TResultObj<DataObj>> CheckSiteHash(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckSiteHash", "Site Content Hash", defaultTimeout: ushort.MaxValue - 1);
        }

        public async Task<TResultObj<DataObj>> CheckSmtp(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckSmtp", "SMTP Hello", defaultPort: 25);
        }

        public async Task<TResultObj<DataObj>> CheckIcmp(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckIcmp", "ICMP Ping");
        }

        public async Task<TResultObj<DataObj>> CheckRawconnect(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckRawconnect", "Raw Socket Connection");
        }

        public async Task<TResultObj<DataObj>> CheckNmap(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckNmap", "Nmap Connection", defaultTimeout: ushort.MaxValue - 1);
        }

        public async Task<TResultObj<DataObj>> CheckNmapVuln(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckNmapVuln", "Nmap Vulnerability Scan", defaultTimeout: ushort.MaxValue - 1);
        }

        public async Task<TResultObj<DataObj>> CheckCrawlSite(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckCrawlSite", "Crawl Site", defaultTimeout: ushort.MaxValue - 1);
        }

        public async Task<TResultObj<DataObj>> CheckDailyCrawl(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckDailyCrawl", "Daily Crawl", defaultTimeout: ushort.MaxValue - 1);
        }

        public async Task<TResultObj<DataObj>> CheckDailyHugKeepAlive(HostObject hostObj)
        {
            return await PerformCheck(hostObj, "CheckDailyHugKeepAlive", "Daily HuggingFace Keep Alive", defaultTimeout: ushort.MaxValue - 1);
        }


        public async Task<TResultObj<DataObj>> CheckDns(HostObject hostObj)
        {
            return await PerformCheck(
                hostObj,
                "CheckDns",
                "DNS Lookup",
                customResultStatusHandler: (netConnect) =>
                {
                    if (netConnect != null && netConnect.MpiConnect != null)
                    {
                        string[]? splitData = netConnect.MpiConnect.PingInfo?.Status?.Split(new char[] { ':' }, 3);
                        if (splitData != null && splitData.Length > 2)
                        {
                            if (netConnect.MpiConnect.IsUp)
                                return "Resolved addresses : " + splitData[2];
                            else
                                return splitData[2];
                        }
                    }
                    return string.Empty;
                });
        }

        public async Task<TResultObj<QuantumDataObj>> CheckQuantum(QuantumHostObject hostObj)
        {
            var result = new TResultObj<QuantumDataObj>();
            result.Message = " SERVICE : CheckQuantum : ";
            try
            {
                var monitorPingInfo = new MonitorPingInfo()
                {
                    Address = hostObj.Address,
                    Port = hostObj.Port,
                    EndPointType = "quantum",
                    Timeout = hostObj.Timeout,
                };

                var netConnect = _netConnectCollection.GetNetConnectInstance(monitorPingInfo);
                await netConnect.Connect();
                result.Message += netConnect.MpiConnect.Message;
                result.Success = netConnect.MpiConnect.IsUp;
                var data = new QuantumDataObj();
                data.TestedUrl = hostObj.Address;
                data.TestedPort = hostObj.Port;
                data.ResultSuccess = netConnect.MpiConnect.IsUp;
                if (netConnect.MpiConnect.PingInfo.RoundTripTime != UInt16.MaxValue)
                    data.ResponseTime = netConnect.MpiConnect.PingInfo.RoundTripTime;
                else
                    data.Timeout = netConnect.MpiStatic.Timeout;
                string[] splitData = result.Message.Split(new char[] { ':' }, 3);
                if (splitData.Length > 5)
                {
                    data.QuantumKeyExchange = splitData[5];
                }
                if (splitData.Length > 2)
                {
                    data.ResultStatus = splitData[2];
                }
                result.Data = data;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message += " Error : " + ex.Message;
                _logger.LogError(result.Message);
            }
            return result;
        }
    }
}
