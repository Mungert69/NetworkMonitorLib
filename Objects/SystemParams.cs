using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class SystemParams
    {
        public SystemParams() { }
        private List<SystemUrl>? _systemUrls = new List<SystemUrl>();

        private SystemUrl _thisSystemUrl = new SystemUrl();
        private string? _systemPassword = "";
        private bool _isSingleSystem;
        private string _emailEncryptKey = "";
        private string _lLMEncryptKey = "";
        private string? _systemUser = "";
        private string? _mailServer = "";
        private int _mailServerPort;
        private bool _mailServerUseSSL;
        private string? _emailSendServerName = "";
        private string? _trustPilotReviewEmail = "";
        private string? _systemEmail = "";
        private string? _publicIPAddress = "";
        private List<string> _enabledRegions = new List<string>();
        private string _defaultRegion = "";
        private int _sendReportsTimeSpan = 48;
        private string _audioServiceUrl;
        private List<string> _audioServiceUrls;
        private string _audioServiceOutputDir;
        private string _dbPassword = "";

        //private string? _rabbitHostName="";
        //private string? _rabbitInstanceName="";
        private ManagementToken _managementToken = new ManagementToken();
        private bool sendTrustPilot;
        private string? _openAIPluginServiceKey;
        private List<string> _rapidApiKeys;
        private string? _serviceID ="Service";
        private string? _userFacingServiceId;
        private string? _serviceAuthKey;
        private int _expireMonths;
        private string _frontEndUrl;
        private ushort _givenAgentPort;
        private string _redisSecret = "";
        private string _redisUrl = "";
        private string _rabbitRoutingKey = "";
        private string _rabbitExchangeType = "fanout";
        private string _dataDir = "data";

        private Dictionary<string, string> _exchangeTypes = new();

        public List<SystemUrl>? SystemUrls { get => _systemUrls; set => _systemUrls = value; }
        public string? SystemPassword { get => _systemPassword; set => _systemPassword = value; }
        public SystemUrl ThisSystemUrl { get => _thisSystemUrl; set => _thisSystemUrl = value; }
        public bool IsSingleSystem { get => _isSingleSystem; set => _isSingleSystem = value; }
        public string EmailEncryptKey { get => _emailEncryptKey; set => _emailEncryptKey = value; }
        public string? SystemUser { get => _systemUser; set => _systemUser = value; }
        public string? MailServer { get => _mailServer; set => _mailServer = value; }
        public int MailServerPort { get => _mailServerPort; set => _mailServerPort = value; }
        public bool MailServerUseSSL { get => _mailServerUseSSL; set => _mailServerUseSSL = value; }
        public string? TrustPilotReviewEmail { get => _trustPilotReviewEmail; set => _trustPilotReviewEmail = value; }
        public string? SystemEmail { get => _systemEmail; set => _systemEmail = value; }
        public string? PublicIPAddress { get => _publicIPAddress; set => _publicIPAddress = value; }
        public ManagementToken ManagementToken { get => _managementToken; set => _managementToken = value; }

        public bool SendTrustPilot { get => sendTrustPilot; set => sendTrustPilot = value; }

        public string? OpenAIPluginServiceKey { get => _openAIPluginServiceKey; set => _openAIPluginServiceKey = value; }
        public string? EmailSendServerName { get => _emailSendServerName; set => _emailSendServerName = value; }
        public string? ServiceID { get => _serviceID; set => _serviceID = value; }
        public string? UserFacingServiceId { get => _userFacingServiceId; set => _userFacingServiceId = value; }
        public string? ServiceAuthKey { get => _serviceAuthKey; set => _serviceAuthKey = value; }
        public int ExpireMonths { get => _expireMonths; set => _expireMonths = value; }
        public List<string> EnabledRegions { get => _enabledRegions; set => _enabledRegions = value; }
        public string DefaultRegion { get => _defaultRegion; set => _defaultRegion = value; }
        public string FrontEndUrl { get => _frontEndUrl; set => _frontEndUrl = value; }
        public int SendReportsTimeSpan { get => _sendReportsTimeSpan; set => _sendReportsTimeSpan = value; }
        public ushort GivenAgentPort { get => _givenAgentPort; set => _givenAgentPort = value; }
        public string AudioServiceUrl { get => _audioServiceUrl; set => _audioServiceUrl = value; }
        public string AudioServiceOutputDir { get => _audioServiceOutputDir; set => _audioServiceOutputDir = value; }
        public string RedisSecret { get => _redisSecret; set => _redisSecret = value; }
        public string RedisUrl { get => _redisUrl; set => _redisUrl = value; }
        public string LLMEncryptKey { get => _lLMEncryptKey; set => _lLMEncryptKey = value; }
        public List<string> RapidApiKeys { get => _rapidApiKeys; set => _rapidApiKeys = value; }
        public string RabbitRoutingKey { get => _rabbitRoutingKey; set => _rabbitRoutingKey = value; }
        public string RabbitExchangeType { get => _rabbitExchangeType; set => _rabbitExchangeType = value; }
        public Dictionary<string, string> ExchangeTypes { get => _exchangeTypes; set => _exchangeTypes = value; }
        public string DbPassword { get => _dbPassword; set => _dbPassword = value; }
        public string DataDir { get => _dataDir; set => _dataDir = value; }
        public List<global::System.String> AudioServiceUrls { get => _audioServiceUrls; set => _audioServiceUrls = value; }
    }
}
