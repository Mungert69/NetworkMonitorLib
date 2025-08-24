using System;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Connection;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;


namespace NetworkMonitor.Connection
{
    public class NetConnectConfig : INotifyPropertyChanged
    {

        private SystemUrl _localSystemUrl = new SystemUrl();
        private string _appID = "";
        private string _appName = "";
        private string _appDataDirectory;
        private string _nativeLibDir = string.Empty;
        private string _googleSearchApiKey;
        private string _googleSearchApiCxID;

        public event Func<SystemUrl, Task>? OnSystemUrlChangedAsync;
        public event Func<string, Task>? OnAppIDChangedAsync;
        public event Func<Task>? OnAuthCompleteAsync;



        public SystemUrl LocalSystemUrl
        {
            get => _localSystemUrl;
            private set
            {
                if (_localSystemUrl != value)
                {
                    _localSystemUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string AppID
        {
            get => _appID;
            private set
            {
                if (_appID != value)
                {
                    _appID = value;
                    OnPropertyChanged();
                }
            }
        }

        public async Task SetLocalSystemUrlAsync(SystemUrl value)
        {

            if (OnSystemUrlChangedAsync != null)
            {
                await OnSystemUrlChangedAsync(value);
            }
            LocalSystemUrl = value;
        }

        public async Task AuthComplete()
        {

            if (OnAuthCompleteAsync != null)
            {
                await OnAuthCompleteAsync();
            }
        }

        public async Task SetAppIDAsync(string value)
        {

            if (OnAppIDChangedAsync != null)
            {
                await OnAppIDChangedAsync(value);
            }
            AppID = value;
        }

        private int _maxTaskQueueSize;
        private string _oqsProviderPath = "";
        private string _commandPath = "";
        private bool _loadChromium = false;
        private string _oqsProviderPathReadOnly = "";
        private string _opensslVersion = "openssl-3.4.2";
        private string _clientAuthUrl = "";
        private string _authKey = "";
        private string _baseFusionAuthURL = "";
        private string _clientId = "";
        private string _owner = "";
        private string _monitorLocation = "Not set - ffffff";
        private string _loadServer = $"loadserver.{AppConstants.AppDomain}";
        private string _serviceServer = $"monitorsrv.{AppConstants.AppDomain}";
        private string _chatServer = $"chatsrv.{AppConstants.AppDomain}";
        private bool _isChatMode = false;
        private bool _useTls = false;
        private int _maxRetries = 3;
        private string _serviceDomain = AppConstants.AppDomain;
        private bool _isRestrictedPublishPerm = true;
        private List<string> _endpointTypes = new List<string>();
        private List<string> _disabledEndpointTypes = new List<string>();
        private List<string> _disabledCommands = new List<string>();
        private string _defaultEndpointType = "imcp";
        private bool _useDefaultEndpointType = false;
        private int _cmdReturnDataLineLimit = 100;
        private bool _forceHeadless = false;
        private int _retryDelayMilliseconds = 10000;
        private string _transcribeAudioUrl;
        private AgentUserFlow _agentUserFlow = new AgentUserFlow();
        private List<FilterStrategyConfig> _filterStrategies = new List<FilterStrategyConfig>();


        public int MaxTaskQueueSize
        {
            get => _maxTaskQueueSize;
            set => SetProperty(ref _maxTaskQueueSize, value);
        }

        public string OqsProviderPath
        {
            get => _oqsProviderPath;
            set => SetProperty(ref _oqsProviderPath, value);
        }

        public string ClientAuthUrl
        {
            get => _clientAuthUrl;
            set => SetProperty(ref _clientAuthUrl, value);
        }

        public string AuthKey
        {
            get => _authKey;
            set => SetProperty(ref _authKey, value);
        }

        public string BaseFusionAuthURL
        {
            get => _baseFusionAuthURL;
            set => SetProperty(ref _baseFusionAuthURL, value);
        }

        public string ClientId
        {
            get => _clientId;
            set => SetProperty(ref _clientId, value);
        }

        public string Owner
        {
            get => _owner;
            set => SetProperty(ref _owner, value);
        }

        public string MonitorLocation
        {
            get => _monitorLocation;
            set => SetProperty(ref _monitorLocation, value);
        }

        public AgentUserFlow AgentUserFlow
        {
            get => _agentUserFlow;
            set => SetProperty(ref _agentUserFlow, value);
        }
        public string LoadServer { get => _loadServer; set => _loadServer = value; }

        public bool UseTls { get => _useTls; set => _useTls = value; }
        public string ServiceDomain { get => _serviceDomain; set => _serviceDomain = value; }
        public string ServiceServer { get => _serviceServer; set => _serviceServer = value; }
        public bool IsRestrictedPublishPerm { get => _isRestrictedPublishPerm; set => _isRestrictedPublishPerm = value; }
        public string OpensslVersion { get => _opensslVersion; set => _opensslVersion = value; }
        public string OqsProviderPathReadOnly { get => _oqsProviderPathReadOnly; }
        public List<string> EndpointTypes { get => _endpointTypes; set => _endpointTypes = value; }

        public List<string> DisabledEndpointTypes { get => _disabledEndpointTypes; set => _disabledEndpointTypes = value; }
        public string DefaultEndpointType { get => _defaultEndpointType; set => _defaultEndpointType = value; }
        public bool UseDefaultEndpointType { get => _useDefaultEndpointType; set => _useDefaultEndpointType = value; }
        public List<string> DisabledCommands { get => _disabledCommands; set => _disabledCommands = value; }
        public int CmdReturnDataLineLimit { get => _cmdReturnDataLineLimit; set => _cmdReturnDataLineLimit = value; }
        public int MaxRetries { get => _maxRetries; set => _maxRetries = value; }

        public string CommandPath { get => _commandPath; set => _commandPath = value; }
        public bool LoadChromium { get => _loadChromium; set => _loadChromium = value; }

        public List<FilterStrategyConfig> FilterStrategies
        {
            get => _filterStrategies;
            set => SetProperty(ref _filterStrategies, value);
        }
        public string GoogleSearchApiKey { get => _googleSearchApiKey; set => _googleSearchApiKey = value; }
        public string GoogleSearchApiCxID { get => _googleSearchApiCxID; set => _googleSearchApiCxID = value; }
        public bool ForceHeadless { get => _forceHeadless; set => _forceHeadless = value; }
        public int RetryDelayMilliseconds { get => _retryDelayMilliseconds; set => _retryDelayMilliseconds = value; }
        public string ChatServer { get => _chatServer; set => _chatServer = value; }
        public bool IsChatMode { get => _isChatMode; set => _isChatMode = value; }
        public string TranscribeAudioUrl { get => _transcribeAudioUrl; set => _transcribeAudioUrl = value; }
        public string NativeLibDir { get => _nativeLibDir;}
        public string AppName { get => _appName; set => _appName = value; }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public NetConnectConfig(IConfiguration config, string appDataDirectory, string nativeLibDir = "")
        {
            try
            {
#pragma warning disable IL2026
                _appDataDirectory = appDataDirectory;
                _nativeLibDir = nativeLibDir;
                BaseFusionAuthURL = config["BaseFusionAuthURL"] ?? "";
                ClientId = config["ClientId"] ?? "";
                LocalSystemUrl = new SystemUrl
                {
                    ExternalUrl = config["LocalSystemUrl:ExternalUrl"] ?? "",
                    IPAddress = config["LocalSystemUrl:IPAddress"] ?? "",
                    RabbitHostName = config["LocalSystemUrl:RabbitHostName"] ?? "",
                    RabbitPort = ushort.TryParse(config["LocalSystemUrl:RabbitPort"], out ushort rabbitPort) ? rabbitPort : (ushort)55671,
                    RabbitInstanceName = config["LocalSystemUrl:RabbitInstanceName"] ?? "",
                    RabbitUserName = config["LocalSystemUrl:RabbitUserName"] ?? "",
                    RabbitPassword = config["LocalSystemUrl:RabbitPassword"] ?? "",
                    RabbitVHost = config["LocalSystemUrl:RabbitVHost"] ?? "",
                    MaxLoad = int.TryParse(config["LocalSystemUrl:MaxLoad"], out int maxLoad) ? maxLoad : 1500,
                    MaxRuntime = int.TryParse(config["LocalSystemUrl:MaxRuntime"], out int maxRuntime) ? maxRuntime : 60,
                };
                AppID = config["AppID"] ?? "";
                AppName = config["AppName"] ?? "";
                LoadServer = config["LoadServer"] ?? $"loadserver.{AppConstants.AppDomain}";
                ServiceServer = config["ServiceServer"] ?? $"monitorsrv.{AppConstants.AppDomain}";
                ChatServer = config["ChatServer"] ?? $"chatsrv.{AppConstants.AppDomain}";
                FilterStrategies = config.GetSection("FilterStrategies").Get<List<FilterStrategyConfig>>() ?? new List<FilterStrategyConfig>();

                MaxTaskQueueSize = int.TryParse(config["MaxTaskQueueSize"], out int maxTaskQueueSize) ? maxTaskQueueSize : 100;
                MaxRetries = int.TryParse(config["MaxRetries"], out int maxRetries) ? maxRetries : 3;

                OqsProviderPath = config["OqsProviderPath"] ?? "";
                CommandPath = config["CommandPath"] ?? "";
                TranscribeAudioUrl = config["TranscribeAudioUrl"] ?? "";
                OqsProviderPath = FixDirPath(OqsProviderPath);
                CommandPath = FixDirPath(CommandPath);
                _oqsProviderPathReadOnly = OqsProviderPath;
                LoadChromium = bool.TryParse(config["LoadChromium"], out bool loadChromium) ? loadChromium : false;
                IsChatMode = bool.TryParse(config["IsChatMode"], out bool isChatMode) ? isChatMode : false;

                CmdReturnDataLineLimit = int.TryParse(config["CmdReturnDataLineLimit"], out int cmdReturnDataLineLimit) ? cmdReturnDataLineLimit : 100;
                RetryDelayMilliseconds = int.TryParse(config["RetryDelayMilliseconds"], out int retryDelayMilliseconds) ? retryDelayMilliseconds : 10000;
                OpensslVersion = config["OpensslVersion"] ?? "openssl";
                AuthKey = config["AuthKey"] ?? "";
                DisabledEndpointTypes = config.GetSection("DisabledEndpointTypes").Get<List<string>>() ?? new List<string>();
                DisabledCommands = config.GetSection("DisabledCommands").Get<List<string>>() ?? new List<string>();

                EndpointTypes = EndPointTypeFactory.GetEnabledEndPoints(DisabledEndpointTypes);
                DefaultEndpointType = config["DefaultEndpointType"] ?? "icmp";
                UseDefaultEndpointType = bool.TryParse(config["UseDefaultEndpointType"], out bool useDefaultEndpointType) ? useDefaultEndpointType : false;
                ServiceDomain = config["ServiceDomain"] ?? AppConstants.AppDomain;
                UseTls = bool.TryParse(config["UseTls"], out bool useTls) ? useTls : false;
                IsRestrictedPublishPerm = bool.TryParse(config["IsRestrictedPublishPerm"], out bool restrictedPublishPerm) ? restrictedPublishPerm : false;
                AgentUserFlow.IsAuthorized = bool.TryParse(config["AgentUserFlow:IsAuthorized"], out bool isAuthorized) ? isAuthorized : false;
                AgentUserFlow.IsLoggedInWebsite = bool.TryParse(config["AgentUserFlow:IsLoggedInWebsite"], out bool isLoggedInWebsite) ? isLoggedInWebsite : false;
                AgentUserFlow.IsHostsAdded = bool.TryParse(config["AgentUserFlow:IsHostsAdded"], out bool isHostsAddded) ? isHostsAddded : false;
                AgentUserFlow.IsChatOpened = bool.TryParse(config["AgentUserFlow:IsChatOpened"], out bool isChatOpened) ? isChatOpened : false;
                ForceHeadless = bool.TryParse(config["ForceHeadless"], out bool forceHeadless) ? forceHeadless : false;
                Owner = config["Owner"] ?? "";
                MonitorLocation = config["MonitorLocation"] ?? "Not set - ffffff";
                GoogleSearchApiKey = config["GoogleSearchApiKey"] ?? "Not set";
                GoogleSearchApiCxID = config["GoogleSearchApiCxID"] ?? "Not set";
#pragma warning restore IL2026
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Error : Could not load config to NetConnectConfig . Error was : {ex.Message}");
            }
        }


        public string FixDirPath(string path)
        {
            if (!path.Contains(_appDataDirectory))
            {
                string[] pathComponents = path.Trim('/').Split('/');

                string fullPath = _appDataDirectory;
                foreach (var component in pathComponents)
                {
                    fullPath = Path.Combine(fullPath, component);
                }
                path = fullPath;
                System.Diagnostics.Debug.WriteLine($" Updated file path to : {path}");


            }
            path = path.TrimEnd('/') + "/";

            return path;
        }
    }
}
