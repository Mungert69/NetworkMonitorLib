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
                    LlmServiceObj = new LLMServiceObj { SessionId = "" }
                });

            Assert.False(result.Success);
            using var response = JsonDocument.Parse(result.Message);
            Assert.Equal(
                "Live Metasploit console error: A non-empty LLM SessionId is required for a live Metasploit console.",
                response.RootElement.GetProperty("error").GetString());
        }
    }
}
