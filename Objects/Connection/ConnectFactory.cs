using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Factory;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using System.Net.Cache;
using System.Net.Http.Headers;
using System;
using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using PuppeteerSharp;

//using PuppeteerSharp;
using Microsoft.Extensions.Logging;


namespace NetworkMonitor.Connection
{
    public interface IConnectFactory
    {

        INetConnect GetNetConnectObj(MonitorPingInfo pingInfo, PingParams pingParams);
        //void UpdateNetConnectObj(MonitorPingInfo monitorPingInfo, PingParams pingParams, INetConnect netConnect);
        void UpdateNetConnectionInfo(INetConnect netConnect, MonitorPingInfo monitorPingInfo, PingParams? pingParams = null);
    }
    public class ConnectFactory : IConnectFactory
    {
        private static ICmdProcessorProvider? _cmdProcessorProvider;
        private HttpClient _httpClient;
        private HttpClient _httpsClient;
        private List<AlgorithmInfo> _algorithmInfoList = new List<AlgorithmInfo>();
        private NetConnectConfig? _netConfig;
        private ILogger _logger;
        private ILaunchHelper? _launchHelper;

        public ConnectFactory(ILogger logger, NetConnectConfig? netConfig = null, ICmdProcessorProvider? cmdProcessorProvider = null, ILaunchHelper? launchHelper = null)
        {
            _logger = logger;
            _netConfig = netConfig;
            _cmdProcessorProvider = cmdProcessorProvider;
            _launchHelper = launchHelper;

            var sockerHttpHandler = new SocketsHttpHandler()
            {
                UseProxy = false,
                AllowAutoRedirect = true,
                UseCookies = true,
            };
            sockerHttpHandler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

            _httpClient = new HttpClient(sockerHttpHandler) { };
            _httpClient.DefaultRequestHeaders.ConnectionClose = true;
            // Set the Accept header to tell the server the types of media that the client can process.
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml", 0.9));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));

            // Set the Accept-Language header to specify the natural languages preferred by the client.
            _httpClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US"));
            _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.9));

            // Set the Accept-Encoding header to specify the content codings that are acceptable in the response.
            _httpClient.DefaultRequestHeaders.AcceptEncoding.Clear();
            _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgents.GetRandomUserAgent());

            var httpsSockerHttpHandler = new SocketsHttpHandler()
            {
                UseProxy = false,
                AllowAutoRedirect = true,
                UseCookies = true,
                // other settings copied from sockerHttpHandler
            };
            httpsSockerHttpHandler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => ServerCertificateValidationCallback(sender, certificate!, chain!, sslPolicyErrors);


            // Initialize _httpsClient with the handler
            _httpsClient = new HttpClient(httpsSockerHttpHandler);

            // Copy headers and settings from _httpClient to _httpsClient
            _httpsClient.DefaultRequestHeaders.ConnectionClose = _httpClient.DefaultRequestHeaders.ConnectionClose;
            _httpsClient.DefaultRequestHeaders.Accept.Clear();
            foreach (var header in _httpClient.DefaultRequestHeaders.Accept)
            {
                _httpsClient.DefaultRequestHeaders.Accept.Add(header);
            }
            _httpsClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            foreach (var header in _httpClient.DefaultRequestHeaders.AcceptLanguage)
            {
                _httpsClient.DefaultRequestHeaders.AcceptLanguage.Add(header);
            }
            _httpsClient.DefaultRequestHeaders.AcceptEncoding.Clear();
            foreach (var header in _httpClient.DefaultRequestHeaders.AcceptEncoding)
            {
                _httpsClient.DefaultRequestHeaders.AcceptEncoding.Add(header);
            }

            _httpsClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgents.GetRandomUserAgent());


            if (netConfig != null && netConfig.OqsProviderPath != null)
            {
                _algorithmInfoList = ConnectHelper.GetAlgorithmInfoList(netConfig);
            }
            else
            {
                _logger.LogWarning(" Warning : Algo table not created Quantum Connect will not fucntion .");
            }
            if (_netConfig.CommandPath == null) throw new ArgumentException(" Command Path not found");
            if (_netConfig.OqsProviderPath == null) throw new ArgumentException(" Lib Path not found");

        }
        /*public Task GetNetConnect(MonitorPingInfo pingInfo, PingParams pingParams)
        {
            return GetNetConnectObj(pingInfo, pingParams).Connect();
        }*/

        public async Task<ResultObj> SetupChromium(NetConnectConfig? netConfig)
        {
            var result = new ResultObj();
            try
            {

                if (netConfig != null && netConfig.LoadChromium && _launchHelper != null)
                {

                    await _launchHelper.GetLauncher(netConfig.CommandPath, _logger);
                    result.Success = true;
                    result.Message = " Success : Chromium Loaded OK";
                }
                else
                {
                    result.Success = false;
                    result.Message = $"Chromium is not loaded, netConfig is null, LoadChromium is not true. or null lanchHelper was passed";

                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Message = $" Error : Failed to Load Chromium . Error was : {e.Message}";

            }
            if (result.Success) _logger.LogInformation(result.Message);
            else _logger.LogError(result.Message);
            return result;

        }
        public void UpdateNetConnectionInfo(INetConnect netConnect, MonitorPingInfo monitorPingInfo, PingParams? pingParams = null)
        {
            if (monitorPingInfo != null)
            {
                netConnect.MpiStatic.MonitorIPID = monitorPingInfo.MonitorIPID;
                netConnect.MpiStatic.Address = AddressFilter.FilterAddress(monitorPingInfo.Address, monitorPingInfo.EndPointType);
                //netConnect.MpiStatic.AppID = monitorPingInfo.AppID;
                //netConnect.MpiStatic.DateStarted = monitorPingInfo.DateStarted;
                netConnect.MpiStatic.Enabled = monitorPingInfo.Enabled;
                // netConnect.MpiStatic.ID = monitorPingInfo.ID;
                netConnect.MpiStatic.Port = monitorPingInfo.Port;
                netConnect.MpiStatic.Timeout = monitorPingInfo.Timeout;
                // netConnect.MpiStatic.UserID = monitorPingInfo.UserID;
                netConnect.MpiStatic.EndPointType = monitorPingInfo.EndPointType;
                netConnect.MpiStatic.Username = monitorPingInfo.Username;
                netConnect.MpiStatic.Password = monitorPingInfo.Password;
                netConnect.MpiStatic.SiteHash = monitorPingInfo.SiteHash;
            }
            //if (pingParams != null) netConnect.PingParams = pingParams;

        }
        private void UpdateNetConnectObj(MonitorPingInfo monitorPingInfo, PingParams pingParams, INetConnect netConnect)
        {
            netConnect.MpiStatic = new MPIStatic(monitorPingInfo);
            netConnect.MpiStatic.Address = AddressFilter.FilterAddress(monitorPingInfo.Address, monitorPingInfo.EndPointType);
            //netConnect.PingParams = pingParams;
        }
        //Method to get the NetConnect object based on what who starts with http or icmp
        public INetConnect GetNetConnectObj(MonitorPingInfo monitorPingInfo, PingParams pingParams)
        {
            if (monitorPingInfo.Timeout > pingParams.Timeout || monitorPingInfo.Timeout == 0) monitorPingInfo.Timeout = pingParams.Timeout;
            string? type = monitorPingInfo.EndPointType;
            INetConnect netConnect = EndPointTypeFactory.CreateNetConnect(type, _httpClient, _httpsClient, _algorithmInfoList,_netConfig.OqsProviderPath, _netConfig.CommandPath!, _logger, _cmdProcessorProvider, _launchHelper, _netConfig.NativeLibDir!  );
            UpdateNetConnectObj(monitorPingInfo, pingParams, netConnect);
            return netConnect;
        }

        private static bool ServerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            //see: https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate.getexpirationdatestring?view=netcore-3.1#remarks
            //Make sure we parse the DateTime.Parse(expirationdate) the same as GetExpirationDateString() does.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var expirationDate = DateTime.Parse(certificate.GetExpirationDateString(), CultureInfo.InvariantCulture);
            if (expirationDate < DateTime.Today)
            {
                throw new HttpRequestException("Certificate expired");
            }
            if (expirationDate - DateTime.Today < TimeSpan.FromDays(7))
            {
                throw new HttpRequestException("Certificate expires within 7 days");
            }

            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }
            else
            {
                throw new HttpRequestException("Cert policy errors: " + sslPolicyErrors.ToString());
            }
        }
    }
}
