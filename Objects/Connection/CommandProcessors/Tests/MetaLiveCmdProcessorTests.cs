using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using System.Text.Json;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests;

public sealed class MetaLiveCmdProcessorTests
{
    [Fact]
    public async Task RunCommand_WithoutSessionId_FailsWithoutStartingMsfconsole()
    {
        var processor = new MetaLiveCmdProcessor(
            Mock.Of<ILogger>(),
            Mock.Of<ILocalCmdProcessorStates>(),
            Mock.Of<IRabbitRepo>(),
            new NetConnectConfig(Mock.Of<IConfiguration>(), "/bin/"));

        using (processor)
        {
            var result = await processor.RunCommand(
                "{\"control\":\"read\"}",
                CancellationToken.None,
                new ProcessorScanDataObj
                {
                    LlmServiceObj = new LLMServiceObj
                    {
                        SessionId = "",
                        UserInfo = new UserInfo { UserID = "authenticated-user" }
                    }
                });

            Assert.False(result.Success);
            using var response = JsonDocument.Parse(result.Message);
            Assert.Equal(
                "Live Metasploit console error: A non-empty LLM SessionId is required for a live Metasploit console.",
                response.RootElement.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    public async Task RunCommand_WithoutAuthenticatedUserId_FailsWithoutStartingMsfconsole(string? userId)
    {
        var processor = new MetaLiveCmdProcessor(
            Mock.Of<ILogger>(),
            Mock.Of<ILocalCmdProcessorStates>(),
            Mock.Of<IRabbitRepo>(),
            new NetConnectConfig(Mock.Of<IConfiguration>(), "/bin/"));

        using (processor)
        {
            var result = await processor.RunCommand(
                "{\"control\":\"read\"}",
                CancellationToken.None,
                new ProcessorScanDataObj
                {
                    LlmServiceObj = new LLMServiceObj
                    {
                        SessionId = "session-1",
                        UserInfo = new UserInfo { UserID = userId }
                    }
                });

            Assert.False(result.Success);
            using var response = JsonDocument.Parse(result.Message);
            Assert.Equal(
                "Live Metasploit console error: An authenticated, non-default UserID is required for a live Metasploit console.",
                response.RootElement.GetProperty("error").GetString());
        }
    }
}
