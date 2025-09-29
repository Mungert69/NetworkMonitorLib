using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class SystemParamsHelperTests
{
    private static SystemParamsHelper CreateHelper(Dictionary<string, string?>? overrides, out string envPath)
    {
        TestUtilities.ResetGetConfigHelper();
        envPath = TestUtilities.CreateTempFile();
        var data = new Dictionary<string, string?>
        {
            { "EnvPath", envPath },
            { "LocalSystemUrl:ExternalUrl", "https://node-1" },
            { "LocalSystemUrl:IPAddress", "127.0.0.1" }
        };

        if (overrides != null)
        {
            foreach (var kvp in overrides)
            {
                data[kvp.Key] = kvp.Value;
            }
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        return new SystemParamsHelper(config, NullLogger<SystemParamsHelper>.Instance);
    }

    [Fact]
    public void GetPingParams_UsesDefaultsWhenMissing()
    {
        var helper = CreateHelper(null, out var envPath);

        try
        {
            var pingParams = helper.GetPingParams();

            Assert.Equal(59000, pingParams.Timeout);
            Assert.Equal(4, pingParams.AlertThreshold);
            Assert.Equal(10, pingParams.HostLimit);
        }
        finally
        {
            File.Delete(envPath);
        }
    }

    [Fact]
    public void GetPingParams_UsesConfiguredValues()
    {
        var helper = CreateHelper(new Dictionary<string, string?>
        {
            { "PingTimeOut ", "1200" },
            { "AlertThreshold ", "2" },
            { "HostLimit ", "15" }
        }, out var envPath);

        try
        {
            var pingParams = helper.GetPingParams();

            Assert.Equal(1200, pingParams.Timeout);
            Assert.Equal(2, pingParams.AlertThreshold);
            Assert.Equal(15, pingParams.HostLimit);
        }
        finally
        {
            File.Delete(envPath);
        }
    }

    [Fact]
    public void GetAlertParams_UsesConfiguration()
    {
        var helper = CreateHelper(new Dictionary<string, string?>
        {
            { "PredictThreshold", "8" },
            { "AlertThreshold ", "3" },
            { "DisableEmails", "true" },
            { "DisablePredictEmailAlert", "true" },
            { "DisableMonitorEmailAlert", "true" },
            { "CheckAlerts", "false" }
        }, out var envPath);

        try
        {
            var alertParams = helper.GetAlertParams();

            Assert.Equal(8, alertParams.PredictThreshold);
            Assert.Equal(3, alertParams.AlertThreshold);
            Assert.True(alertParams.DisableEmails);
            Assert.True(alertParams.DisablePredictEmailAlert);
            Assert.True(alertParams.DisableMonitorEmailAlert);
            Assert.False(alertParams.CheckAlerts);
        }
        finally
        {
            File.Delete(envPath);
        }
    }

    [Fact]
    public void GetMLParams_ReadsConfiguredValues()
    {
        var helper = CreateHelper(new Dictionary<string, string?>
        {
            { "PredictWindow", "123" },
            { "SpikeDetectionThreshold", "7" },
            { "ChangeConfidence", "55" },
            { "LlmRunnerRoutingKeys:TurboLLM", "execute.mock" }
        }, out var envPath);

        try
        {
            var mlParams = helper.GetMLParams();

            Assert.Equal(123, mlParams.PredictWindow);
            Assert.Equal(7, mlParams.SpikeDetectionThreshold);
            Assert.Equal(55, mlParams.ChangeConfidence);
            Assert.Equal("execute.mock", mlParams.LlmRunnerRoutingKeys["TurboLLM"]);
        }
        finally
        {
            File.Delete(envPath);
        }
    }

    [Fact]
    public void GetSystemParams_SetsPropertiesAndUsesEnvOverrides()
    {
        var overrides = new Dictionary<string, string?>
        {
            { "SystemUrls:0:ExternalUrl", "https://node-1" },
            { "SystemUrls:0:IPAddress", "10.0.0.1" },
            { "SystemUrls:1:ExternalUrl", "https://node-2" },
            { "SystemUrls:1:IPAddress", "10.0.0.2" },
            { "EnabledRegions:0", "Europe" },
            { "EnabledRegions:1", "America" },
            { "AudioServiceUrls:0", "https://audio" },
            { "FrontEndUrl", "https://frontend" },
            { "DefaultRegion", "Europe" },
            { "SystemEmail", "support@example.com" },
            { "MailServer", "mail.example.com" },
            { "RabbitPassword", ".env" }
        };

        var helper = CreateHelper(overrides, out var envPath);
        var originalProxy = Environment.GetEnvironmentVariable("https_proxy");
        Environment.SetEnvironmentVariable("https_proxy", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("RabbitPassword", "secret");

        try
        {
            var systemParams = helper.GetSystemParams();

            Assert.False(systemParams.IsSingleSystem);
            Assert.Equal("secret", systemParams.ThisSystemUrl.RabbitPassword);
            Assert.Equal("support@example.com", systemParams.SystemEmail);
            Assert.Contains("Europe", systemParams.EnabledRegions);
            Assert.Equal("Europe", systemParams.DefaultRegion);
            Assert.Equal("https://frontend", systemParams.FrontEndUrl);
            Assert.Equal("mail.example.com", systemParams.MailServer);
        }
        finally
        {
            Environment.SetEnvironmentVariable("https_proxy", originalProxy);
            Environment.SetEnvironmentVariable("RabbitPassword", null);
            File.Delete(envPath);
        }
    }
}
