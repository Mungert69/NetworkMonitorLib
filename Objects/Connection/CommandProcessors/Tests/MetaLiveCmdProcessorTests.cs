using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MessagePack;
using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests;

public sealed class MetaLiveCmdProcessorTests
{
    [Fact]
    public async Task InteractAsync_WriteWaitsForOutputAfterInitialIdleRead()
    {
        using var process = StartSleepingProcess();
        using var rpcClient = new ScriptedMetasploitRpcClient(
            Response("", "msf6 > ", false),
            Response("RHOSTS => 172.30.50.10\n", "msf6 > ", false),
            Response("", "msf6 > ", false));
        using var session = new LiveMetasploitSession(process, rpcClient, "1", _ => { });

        var result = await session.InteractAsync(
            new LiveMetasploitRequest
            {
                Input = "set RHOSTS 172.30.50.10",
                Control = "write",
                WaitSeconds = 2
            },
            CancellationToken.None);

        Assert.Equal("RHOSTS => 172.30.50.10\n", result.Output);
        Assert.False(result.Busy);
        Assert.Equal(1, rpcClient.WriteCount);
        Assert.True(rpcClient.ReadCount >= 3);
        process.Kill();
        await process.WaitForExitAsync();
    }

    [Fact]
    public async Task InteractAsync_WriteWaitsUntilBusyConsoleBecomesIdle()
    {
        using var process = StartSleepingProcess();
        using var rpcClient = new ScriptedMetasploitRpcClient(
            Response("", "msf6 > ", false),
            Response("starting\n", "", true),
            Response("finished\n", "msf6 > ", false),
            Response("", "msf6 > ", false));
        using var session = new LiveMetasploitSession(process, rpcClient, "1", _ => { });

        var result = await session.InteractAsync(
            new LiveMetasploitRequest { Input = "run", Control = "write", WaitSeconds = 2 },
            CancellationToken.None);

        Assert.Equal("starting\nfinished\n", result.Output);
        Assert.False(result.Busy);
        process.Kill();
        await process.WaitForExitAsync();
    }

    [Fact]
    public void DecodeResponse_AcceptsBinaryKeysAndTextValues()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(3);
        writer.Write(Encoding.UTF8.GetBytes("result"));
        writer.Write(Encoding.UTF8.GetBytes("success"));
        writer.Write(Encoding.UTF8.GetBytes("token"));
        writer.Write(Encoding.UTF8.GetBytes("generated-token"));
        writer.Write(Encoding.UTF8.GetBytes("busy"));
        writer.Write(false);
        writer.Flush();

        var response = MetasploitRpcClient.DecodeResponse(buffer.WrittenMemory);

        Assert.Equal("success", MetasploitRpcClient.GetString(response, "result"));
        Assert.Equal("generated-token", MetasploitRpcClient.GetString(response, "token"));
        Assert.False(MetasploitRpcClient.GetBoolean(response, "busy"));
    }

    [Fact]
    public void DecodeResponse_PreservesStringKeysAndValues()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);
        writer.Write("id");
        writer.Write("1");
        writer.Write("prompt");
        writer.Write("msf6 > ");
        writer.Flush();

        var response = MetasploitRpcClient.DecodeResponse(buffer.WrittenMemory);

        Assert.Equal("1", MetasploitRpcClient.GetString(response, "id"));
        Assert.Equal("msf6 > ", MetasploitRpcClient.GetString(response, "prompt"));
    }

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

    private static Process StartSleepingProcess()
    {
        return Process.Start(new ProcessStartInfo("sleep", "30")
        {
            RedirectStandardInput = true,
            UseShellExecute = false
        })!;
    }

    private static Dictionary<string, object?> Response(string data, string prompt, bool busy) =>
        new()
        {
            ["data"] = data,
            ["prompt"] = prompt,
            ["busy"] = busy
        };

    private sealed class ScriptedMetasploitRpcClient : IMetasploitRpcClient
    {
        private readonly Queue<Dictionary<string, object?>> _responses;
        private Dictionary<string, object?> _lastResponse;

        public ScriptedMetasploitRpcClient(params Dictionary<string, object?>[] responses)
        {
            _responses = new Queue<Dictionary<string, object?>>(responses);
            _lastResponse = responses[^1];
        }

        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }

        public Task LoginAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Dictionary<string, object?>> CreateConsoleAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Response("", "msf6 > ", false));

        public Task<Dictionary<string, object?>> ReadConsoleAsync(string consoleId, CancellationToken cancellationToken)
        {
            ReadCount++;
            if (_responses.Count > 0)
            {
                _lastResponse = _responses.Dequeue();
            }
            return Task.FromResult(_lastResponse);
        }

        public Task WriteConsoleAsync(string consoleId, string input, CancellationToken cancellationToken)
        {
            WriteCount++;
            return Task.CompletedTask;
        }

        public Task DetachSessionAsync(string consoleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InterruptSessionAsync(string consoleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DestroyConsoleAsync(string consoleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
