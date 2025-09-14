using NetworkMonitor.Objects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Net.Http;
using RestSharp;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects.Factory;
using DotNetEnv;

namespace NetworkMonitor.Utils.Helpers
{
    public interface ISystemParamsHelper
    {
        string GetPublicIP();
        SystemParams GetSystemParams();
        PingParams GetPingParams();

        AlertParams GetAlertParams();

        MLParams GetMLParams();
    }

    public class SystemParamsHelper : ISystemParamsHelper

    {
        // P/Invoke declaration for Unix systems
        [DllImport("libc")]
        private static extern uint geteuid();

        // Checks if the current process is running with root privileges on Unix-like systems
        private static bool IsUnixRoot() => geteuid() == 0;

#if WINDOWS
        // Checks if the current process is running with Administrator privileges on Windows
        private static bool IsWindowsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
#endif
        public static bool IsSystemElevatedPrivilege
        {
            get
            {

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
#if WINDOWS
                    return IsWindowsAdministrator();
#else
                    return false;
#endif
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Works on macOS due to Unix compatibility
                    return IsUnixRoot();
                }
                else
                {
                    return false;
                }
            }
        }

        private readonly IConfiguration _config;
        private readonly ILogger _logger;

        public SystemParamsHelper(IConfiguration config, ILogger<SystemParamsHelper> logger)
        {
            _config = config;
            _logger = logger;
            string envFilePath = config["EnvPath"] ?? ".env";

            if (File.Exists(envFilePath))
            {
                DotNetEnv.Env.Load(envFilePath); // Load from custom path
                _logger.LogInformation($"Loaded environment variables from: {envFilePath}");
            }
            else
            {
                _logger.LogWarning($"No .env file found at: {envFilePath}");
            }
        }
        public string GetPublicIP()
        {
            string publicIp = "IP Address unavailable";
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5); // Set timeout to 5 seconds
                    HttpResponseMessage response = client.GetAsync("https://api.ipify.org/").Result;
                    publicIp = response.Content.ReadAsStringAsync().Result;
                    _logger.LogInformation("Public IP address of this service is " + publicIp);
                }
            }
            catch (AggregateException ae) when (ae.InnerException is TaskCanceledException)
            {
                _logger.LogCritical("Request to get Public IP timed out.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical("Can not get Public IP in MonitorPingService.GetPublicIP : Error was : " + ex.Message);
            }
            return publicIp;
        }
        public SystemParams GetSystemParams()
        {
            SystemParams systemParams = new SystemParams();
#pragma warning disable IL2026
            systemParams.SystemUrls = _config.GetSection("SystemUrls").Get<List<SystemUrl>>() ?? new List<SystemUrl>();
            systemParams.EnabledRegions = _config.GetSection("EnabledRegions").Get<List<string>>() ?? new List<string>();
            systemParams.FrontEndUrl = _config.GetValue<string>("FrontEndUrl") ?? AppConstants.FrontendUrl;
            systemParams.DefaultRegion = _config.GetValue<string>("DefaultRegion") ?? "Europe";
            systemParams.SystemEmail = _config.GetValue<string>("SystemEmail") ?? $"support@{AppConstants.MailDomain}";
            systemParams.SystemUser = _config.GetValue<string>("SystemUser") ?? "support";
            systemParams.MailServer = _config.GetValue<string>("MailServer") ?? $"mail.{AppConstants.MailDomain}";
            systemParams.MailServerPort = _config.GetValue<int?>("MailServerPort") ?? 587;
            systemParams.ExpireMonths = _config.GetValue<int?>("ExpireMonths") ?? 3;
            systemParams.MailServerUseSSL = _config.GetValue<bool?>("MailServerUseSSL") ?? true;
            systemParams.EmailSendServerName = _config.GetValue<string?>("EmailSendServerName") ?? $"monitorsrv.{AppConstants.MailDomain}";
            systemParams.TrustPilotReviewEmail = _config.GetValue<string>("TrustPilotReviewEmail") ?? "Missing";
            systemParams.SendTrustPilot = _config.GetValue<bool?>("SendTrustPilot") ?? true;
            systemParams.SendReportsTimeSpan = _config.GetValue<int?>("SendReportsTimeSpan") ?? 48;
            systemParams.ServiceID = _config.GetValue<string?>("ServiceID");
            systemParams.AudioServiceUrl = _config.GetValue<string?>("AudioServiceUrl") ?? $"https://transcribe.{AppConstants.AppDomain}";
            systemParams.AudioServiceOutputDir = _config.GetValue<string?>("AudioServiceOutputDir") ?? "/home/audioservice/code/securefiles/mail/output_audio";
            systemParams.GivenAgentPort = _config.GetValue<ushort?>("GivenAgentPort") ?? 55671;
            systemParams.RedisUrl = _config.GetValue<string?>("RedisUrl") ?? $"redis.{AppConstants.AppDomain}";
            systemParams.RabbitRoutingKey = _config.GetValue<string?>("RabbitRoutingKey") ?? "";
            systemParams.RabbitExchangeType = _config.GetValue<string?>("RabbitExhangeType") ?? "fanout";
            systemParams.DataDir = _config.GetValue<string?>("DataDir") ?? "data";
            systemParams.ExchangeTypes = _config.GetSection("RabbitMQ:ExchangeTypes").Get<Dictionary<string, string>>() ?? new();

            systemParams.SystemPassword = GetConfigHelper.GetConfigValue(_logger,_config,"SystemPassword", "Missing");
            systemParams.EmailEncryptKey = GetConfigHelper.GetConfigValue(_logger,_config,"EmailEncryptKey", "Missing");
            systemParams.LLMEncryptKey = GetConfigHelper.GetConfigValue(_logger,_config,"LLMEncryptKey", "Missing");
            systemParams.OpenAIPluginServiceKey = GetConfigHelper.GetConfigValue(_logger,_config,"OpenAIPluginServiceKey", "Missing");
            systemParams.RapidApiKey = GetConfigHelper.GetConfigValue(_logger,_config,"RapidApiKey", "Missing");
            systemParams.ServiceAuthKey = GetConfigHelper.GetConfigValue(_logger,_config,"ServiceAuthKey");
            string rabbitPassword = GetConfigHelper.GetConfigValue(_logger,_config,"RabbitPassword", "");
            systemParams.RedisSecret = GetConfigHelper.GetConfigValue(_logger,_config,"REDIS_PASSWORD");
            systemParams.DbPassword = GetConfigHelper.GetConfigValue(_logger,_config,"DB_PASSWORD");
            systemParams.PublicIPAddress = GetPublicIP();
            systemParams.IsSingleSystem = true;
            if (systemParams.SystemUrls != null && systemParams.SystemUrls.Count > 1) systemParams.IsSingleSystem = false;
            systemParams.ThisSystemUrl = _config.GetSection("LocalSystemUrl").Get<SystemUrl>() ?? throw new Exception(" Check config no LocalSystemUrl found");
            if (!string.IsNullOrEmpty(rabbitPassword))
            {
                systemParams.ThisSystemUrl.RabbitPassword = rabbitPassword;
            }
            _logger.LogInformation(" Info : Config ExtermalUrl = " + systemParams.ThisSystemUrl.ExternalUrl + " Config IP address = " + systemParams.ThisSystemUrl.IPAddress + " Found public IP address " + systemParams.PublicIPAddress);

            return systemParams;
        }

       
        public PingParams GetPingParams()
        {
            var pingParams = new PingParams();
            pingParams.Timeout = int.TryParse(_config["PingTimeOut "], out int pingTimeOut) ? pingTimeOut : 59000;
            pingParams.AlertThreshold = int.TryParse(_config["AlertThreshold "], out int alertThreshold) ? alertThreshold : 4;
            pingParams.HostLimit = int.TryParse(_config["HostLimit "], out int hostLimit) ? hostLimit : 10;

            return pingParams;

        }
        public AlertParams GetAlertParams()
        {
            var alertParams = new AlertParams();
            alertParams.PredictThreshold = int.TryParse(_config["PredictThreshold"], out int predictThreshold) ? predictThreshold : 0;
            alertParams.AlertThreshold = int.TryParse(_config["AlertThreshold "], out int alertThreshold) ? alertThreshold : 4;
            alertParams.CheckAlerts = _config.GetValue<bool?>("CheckAlerts") ?? true;
            alertParams.DisableEmails = _config.GetValue<bool?>("DisableEmails") ?? false;
            alertParams.DisablePredictEmailAlert = _config.GetValue<bool?>("DisablePredictEmailAlert") ?? false;
            alertParams.DisableMonitorEmailAlert = _config.GetValue<bool?>("DisableMonitorEmailAlert") ?? false;
#pragma warning restore IL2026
            return alertParams;

        }

        public MLParams GetMLParams()
        {
            var mlParams = new MLParams();

            mlParams.PredictWindow = int.TryParse(_config["PredictWindow"], out int predictWindow) ? predictWindow : 300;
            mlParams.SpikeDetectionThreshold = int.TryParse(_config["SpikeDetectionThreshold"], out int spikeDetectionThreshold) ? spikeDetectionThreshold : 5;
            mlParams.ChangeConfidence = double.TryParse(_config["ChangeConfidence"], out double changeConfidence) ? changeConfidence : 90;
            mlParams.SpikeConfidence = double.TryParse(_config["SpikeConfidence"], out double spikeConfidence) ? spikeConfidence : 99;
            mlParams.ChangePreTrain = int.TryParse(_config["ChangePreTrain"], out int changePreTrain) ? changePreTrain : 50;
            mlParams.SpikePreTrain = int.TryParse(_config["SpikePreTrain"], out int spikePreTrain) ? spikePreTrain : 50;
            mlParams.MaxTokenLengthCap = int.TryParse(_config["MaxTokenLengthCap"], out int maxTokenLengthCap) ? maxTokenLengthCap : 4096;
            mlParams.MinTokenLengthCap = int.TryParse(_config["MinTokenLengthCap"], out int minTokenLengthCap) ? minTokenLengthCap : 128;

            mlParams.LlmModelPath = _config.GetValue<string>("LlmModelPath") ?? "";
            mlParams.LlmVersion = _config.GetValue<string>("LlmVersion") ?? "";
            mlParams.LlmPromptMode = _config.GetValue<string>("LlmPromptMode") ?? "-if -sp";
            mlParams.LlmModelFileName = _config.GetValue<string>("LlmModelFileName") ?? "";
            mlParams.LlmReversePrompt = _config.GetValue<string>("LlmReversePrompt") ?? "";
            mlParams.LlmContextFileName = _config.GetValue<string>("LlmContextFileName") ?? "";
            mlParams.LlmHFModelID = _config.GetValue<string>("LlmHFModelID") ?? "";
            mlParams.LlmSpaceModelID = _config.GetValue<string>("LlmSpaceModelID") ?? "";
            mlParams.LlmProvider = _config.GetValue<string>("LlmProvider") ?? "OpenAI";
            mlParams.LlmHFKey = GetConfigHelper.GetConfigValue(_logger,_config,"LlmHFKey");
            mlParams.DataRepoId = _config.GetValue<string>("DataRepoId") ?? "";
            mlParams.HFToken = GetConfigHelper.GetConfigValue(_logger,_config,"HF_TOKEN");
            mlParams.OpenAIApiKey = GetConfigHelper.GetConfigValue(_logger,_config,"OpenAIApiKey");
            mlParams.LlmHFUrl = _config.GetValue<string>("LlmHFUrl") ?? "";
            mlParams.LlmSystemPrompt = _config.GetValue<string>("LlmSystemPrompt") ?? "";
            mlParams.LlmThreads = int.TryParse(_config["LlmThreads"], out int llmThreads) ? llmThreads : 2;
            mlParams.LlmSystemPromptTimeout = int.TryParse(_config["LlmSystemPromptTimeout"], out int llmSystemPromptTimeout) ? llmSystemPromptTimeout : 10;
            mlParams.LlmCtxSize = int.TryParse(_config["LlmCtxSize"], out int llmCtxSize) ? llmCtxSize : 12000;
            mlParams.LlmResponseTokens = int.TryParse(_config["LlmResponseTokens"], out int llmResponseTokens) ? llmCtxSize : 4000;
            mlParams.LlmRunnerRoutingKeys = _config.GetSection("LlmRunnerRoutingKeys").Get<Dictionary<string, string>>()
                ?? new Dictionary<string, string>
                {
                    { "TurboLLM", "execute.api" },
                    { "HugLLM",   "execute.api" },
                    { "TestLLM",  "execute.local" }
                };
            mlParams.PromptCacheDiscountFraction = decimal.TryParse(_config["PromptCacheDiscountFraction"], out var d) ? d : 0.90m;
            mlParams.MaxFunctionCallsInARow = int.TryParse(_config["MaxFunctionCallsInARow"], out int maxFuncCalls) ? maxFuncCalls : 10;
            mlParams.CompletionCostMultiplier = decimal.TryParse(_config["CompletionCostMultiplier"], out var k) ? k : 8.00m;
            mlParams.DefaultAgentLocation = _config.GetValue<string>("DefaultAgentLocation") ?? "Scanner - EU";
            mlParams.LlmTemp = _config.GetValue<string>("LlmTemp") ?? "0.1";

            mlParams.LlmOpenAICtxSize = int.TryParse(_config["LlmOpenAICtxSize"], out int llmOpenAICtxSize) ? llmOpenAICtxSize : 32000;
            mlParams.LlmCtxRatio = int.TryParse(_config["LlmCtxRatio"], out int llmCtxRatio) ? llmCtxRatio : 6;

            mlParams.StartThisTestLLM = _config.GetValue<bool?>("StartThisTestLLM") ?? true;
            mlParams.LlmNoThink = _config.GetValue<bool?>("LlmNoThink") ?? false;
            mlParams.LlmPromptTokens = int.TryParse(_config["LlmPromptTokens"], out int llmPromptTokens) ? llmPromptTokens : 28000;
            mlParams.LlmGptModel = _config.GetValue<string>("LlmGptModel") ?? "gpt-4o-mini";
            mlParams.LlmUserPromptTimeout = int.TryParse(_config["LlmUserPromptTimeout"], out int llmUserPromptTimeout) ? llmUserPromptTimeout : 50;
            mlParams.LlmSessionIdleTimeout = int.TryParse(_config["LlmSessionIdleTimeout"], out int llmSessionIdleTimeout) ? llmSessionIdleTimeout : 60;
            mlParams.LlmFunctionDic = _config.GetSection("LlmFunctionMapping").Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();
            mlParams.LlmAgentDic = _config.GetSection("LlmAgentMapping").Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();
            mlParams.LlmStartName = _config.GetValue<string>("LlmStartName") ?? "monitor";
            mlParams.LlmNoInitMessage = _config.GetValue<bool?>("LlmNoInitMessage") ?? false;
            mlParams.LlmUseHF = _config.GetValue<bool?>("LlmUseHF") ?? false;
            mlParams.LlmReportProcess = _config.GetValue<bool?>("LlmReportProcess") ?? false;
            mlParams.XmlFunctionParsing = _config.GetValue<bool?>("XmlFunctionParsing") ?? false;
            mlParams.LlmRunnerType = _config.GetValue<string>("LlmRunnerType") ?? "TurboLLM";
            mlParams.LlmHFModelVersion = _config.GetValue<string>("LlmHFModelVersion") ?? "";
            mlParams.AddSystemRag = _config.GetValue<bool?>("AddSystemRag") ?? false;
            mlParams.AddFunctionRag = _config.GetValue<bool?>("AddFunctionRag") ?? false;
            mlParams.IsStream = _config.GetValue<bool?>("IsStream") ?? false;
            mlParams.EnableAgentFlow = _config.GetValue<bool?>("EnableAgentFlow") ?? false;
            mlParams.EmbeddingModelDir = _config.GetValue<string>("EmbeddingModelDir") ?? "stsb-bert-tiny-onnx";
            mlParams.EmbeddingModelVecDim = int.TryParse(_config["EmbeddingModelVecDim"], out int bertModelVecDim) ? bertModelVecDim : 128;
            mlParams.OpenSearchKey = GetConfigHelper.GetConfigValue(_logger,_config,"OpenSearchKey");
            mlParams.OpenSearchUser = _config.GetValue<string>("OpenSearchUser") ?? "admin";
            mlParams.OpenSearchDefaultIndex = _config.GetValue<string>("OpenSearchDefaultIndex") ?? "documents";
            mlParams.OpenSearchUrl = _config.GetValue<string>("OpenSearchUrl") ?? "https://opensearch:9200";
            mlParams.SetVectorSearchModeFromString(_config.GetValue<string>("VectorSearchMode") ?? "content");
            // Embedding provider config
            mlParams.EmbeddingProvider = _config.GetValue<string>("EmbeddingProvider") ?? "local";
            mlParams.LlmHFKey = GetConfigHelper.GetConfigValue(_logger,_config,"LlmHFKey");
            mlParams.EmbeddingApiModel = _config.GetValue<string>("EmbeddingApiModel") ?? "baai/bge-m3";
            mlParams.EmbeddingApiUrl = _config.GetValue<string>("EmbeddingApiUrl") ?? "https://api.novita.ai/v3/openai/embeddings";


#pragma warning restore IL2026
            return mlParams;

        }

    }

}
