using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Security;
using Xunit;

namespace NetworkMonitorLib.Tests.Security;

public class ProtectedConfigManagerTests
{
    private static (ProtectedConfigManager manager, NetConnectConfig netConfig, Mock<IEnvironmentStore> envStoreMock, Mock<IFileRepo> fileRepoMock)
        CreateManager(Dictionary<string, string?> configurationData)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var envStoreMock = new Mock<IEnvironmentStore>();
        envStoreMock.SetupGet(e => e.EnvFilePath).Returns(".env");
        envStoreMock.Setup(e => e.LoadIntoProcess());

        var fileRepoMock = new Mock<IFileRepo>();
        fileRepoMock.Setup(repo => repo.CheckFileExists(It.IsAny<string>(), It.IsAny<ILogger>()));
        fileRepoMock
            .Setup(repo => repo.SaveStateJsonAsync("appsettings.json", It.IsAny<NetConnectConfig>()))
            .Returns(Task.CompletedTask);

        var logger = Mock.Of<ILogger<ProtectedConfigManager>>();
        var manager = new ProtectedConfigManager(configuration, envStoreMock.Object, fileRepoMock.Object, logger);
        var netConfig = new NetConnectConfig(configuration, string.Empty);

        return (manager, netConfig, envStoreMock, fileRepoMock);
    }

    [Fact]
    public async Task SynchronizeSensitiveValuesAsync_MigratesPlaintextValues()
    {
        var configData = new Dictionary<string, string?>
        {
            ["AppID"] = "app",
            ["AuthKey"] = "auth-secret",
            ["LocalSystemUrl:RabbitPassword"] = "rabbit-secret",
            ["LocalSystemUrl:RabbitHostName"] = "localhost",
            ["LocalSystemUrl:RabbitInstanceName"] = "instance",
            ["LocalSystemUrl:RabbitPort"] = "5672",
            ["LocalSystemUrl:ExternalUrl"] = "https://example.test",
            ["LocalSystemUrl:IPAddress"] = "127.0.0.1"
        };

        var (manager, netConfig, envStoreMock, fileRepoMock) = CreateManager(configData);

        string? savedAuthKey = null;
        string? savedRabbitPassword = null;

        fileRepoMock
            .Setup(repo => repo.SaveStateJsonAsync("appsettings.json", It.IsAny<NetConnectConfig>()))
            .Returns(Task.CompletedTask)
            .Callback<string, NetConnectConfig>((_, cfg) =>
            {
                savedAuthKey = cfg.AuthKey;
                savedRabbitPassword = cfg.RabbitPassword;
            });

        await manager.SynchronizeSensitiveValuesAsync(
            netConfig,
            ProtectedConfigurationParameters.All,
            CancellationToken.None);

        envStoreMock.Verify(
            e => e.SetAsync("AuthKey", "auth-secret", It.IsAny<CancellationToken>()),
            Times.Once());
        envStoreMock.Verify(
            e => e.SetAsync("RabbitPassword", "rabbit-secret", It.IsAny<CancellationToken>()),
            Times.Once());

        fileRepoMock.Verify(
            repo => repo.SaveStateJsonAsync("appsettings.json", It.IsAny<NetConnectConfig>()),
            Times.Once());

        Assert.Equal(".env", savedAuthKey);
        Assert.Equal(".env", savedRabbitPassword);
        Assert.Equal("auth-secret", netConfig.AuthKey);
        Assert.Equal("rabbit-secret", netConfig.RabbitPassword);
    }

    [Fact]
    public async Task SynchronizeSensitiveValuesAsync_PopulatesMissingEnvironmentFromRuntime()
    {
        var configData = new Dictionary<string, string?>
        {
            ["AppID"] = "app",
            ["AuthKey"] = ".env",
            ["LocalSystemUrl:RabbitPassword"] = ".env",
            ["LocalSystemUrl:RabbitHostName"] = "localhost",
            ["LocalSystemUrl:RabbitInstanceName"] = "instance",
            ["LocalSystemUrl:RabbitPort"] = "5672",
            ["LocalSystemUrl:ExternalUrl"] = "https://example.test",
            ["LocalSystemUrl:IPAddress"] = "127.0.0.1"
        };

        var (manager, netConfig, envStoreMock, fileRepoMock) = CreateManager(configData);

        netConfig.AuthKey = "runtime-auth";
        netConfig.RabbitPassword = "runtime-rabbit";

        await manager.SynchronizeSensitiveValuesAsync(
            netConfig,
            ProtectedConfigurationParameters.All,
            CancellationToken.None);

        envStoreMock.Verify(
            e => e.SetAsync("AuthKey", "runtime-auth", It.IsAny<CancellationToken>()),
            Times.Once());
        envStoreMock.Verify(
            e => e.SetAsync("RabbitPassword", "runtime-rabbit", It.IsAny<CancellationToken>()),
            Times.Once());

        fileRepoMock.Verify(
            repo => repo.SaveStateJsonAsync("appsettings.json", It.IsAny<NetConnectConfig>()),
            Times.Never());

        Assert.Equal("runtime-auth", netConfig.AuthKey);
        Assert.Equal("runtime-rabbit", netConfig.RabbitPassword);
    }
}
