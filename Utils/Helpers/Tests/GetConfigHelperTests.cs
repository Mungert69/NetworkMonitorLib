using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class GetConfigHelperTests
{
    [Fact]
    public void GetConfigValue_WithoutInitialize_Throws()
    {
        TestUtilities.ResetGetConfigHelper();

        Assert.Throws<InvalidOperationException>(() => GetConfigHelper.GetConfigValue("Missing"));
    }

    [Fact]
    public void GetConfigValue_ReturnsConfigEntry()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiKey", "from-config" } })
            .Build();

        TestUtilities.ResetGetConfigHelper();
        GetConfigHelper.Initialize(config);

        var value = GetConfigHelper.GetConfigValue("ApiKey");

        Assert.Equal("from-config", value);
    }

    [Fact]
    public void GetConfigValue_FallsBackToEnvironment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ApiKey", ".env" } })
            .Build();

        Environment.SetEnvironmentVariable("ApiKey", "from-env");
        TestUtilities.ResetGetConfigHelper();
        GetConfigHelper.Initialize(config);

        var value = GetConfigHelper.GetConfigValue("ApiKey", "default");

        Assert.Equal("from-env", value);

        Environment.SetEnvironmentVariable("ApiKey", null);
    }

    [Fact]
    public void GetSection_LoadsValuesFromEnvironment()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Regions:0", "Europe" }, { "Endpoints", ".env" } })
            .Build();

        Environment.SetEnvironmentVariable("Endpoints", "one,two");
        TestUtilities.ResetGetConfigHelper();
        GetConfigHelper.Initialize(config);

        var section = GetConfigHelper.GetSection("Endpoints");

        var children = section.GetChildren().Select(child => child.Value).ToArray();
        Assert.Equal(new[] { "one", "two" }, children);

        var regions = GetConfigHelper.GetSection("Regions").GetChildren().Select(child => child.Value).ToArray();
        Assert.Equal(new[] { "Europe" }, regions);

        Environment.SetEnvironmentVariable("Endpoints", null);
    }

    [Fact]
    public void GetSection_ChildValueEnv_Supported()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "RemoteCache:ApiKey", ".env" } })
            .Build();

        Environment.SetEnvironmentVariable("ApiKey", "from-env");
        TestUtilities.ResetGetConfigHelper();
        GetConfigHelper.Initialize(config);

        var section = GetConfigHelper.GetSection("RemoteCache");
        var apiKey = section.GetValue<string>("ApiKey", "");

        Assert.Equal("from-env", apiKey);

        Environment.SetEnvironmentVariable("ApiKey", null);
    }

    [Fact]
    public void GetConfigValue_LoadsFromEnvFile_WhenConfiguredAsDotEnv()
    {
        var envFilePath = TestUtilities.CreateTempFile("PfxKey=from-env-file");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "EnvPath", envFilePath },
                { "PfxKey", ".env" }
            })
            .Build();

        Environment.SetEnvironmentVariable("PfxKey", null);
        TestUtilities.ResetGetConfigHelper();
        GetConfigHelper.Initialize(config);

        try
        {
            var value = GetConfigHelper.GetConfigValue("PfxKey", "default");
            Assert.Equal("from-env-file", value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PfxKey", null);
            if (File.Exists(envFilePath))
            {
                File.Delete(envFilePath);
            }
        }
    }
}
