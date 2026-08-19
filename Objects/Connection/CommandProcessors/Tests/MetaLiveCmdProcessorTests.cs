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
    public async Task InteractAsync_LargeOutputReturnsBoundedHeadAndTailWithoutLeakingIntoNextCall()
    {
        const string head = "HEAD-SENTINEL";
        const string tail = "TAIL-SENTINEL";
        var largeOutput = head + new string('x', 20 * 1024) + tail;
        using var process = StartSleepingProcess();
        using var rpcClient = new ScriptedMetasploitRpcClient(
            Response(largeOutput, "msf6 > ", false),
            Response("", "msf6 > ", false),
            Response("", "msf6 > ", false),
            Response("", "msf6 > ", false));
        using var session = new LiveMetasploitSession(process, rpcClient, "1", _ => { });

        var result = await session.InteractAsync(
            new LiveMetasploitRequest { Input = "show payloads", Control = "write", WaitSeconds = 2 },
            CancellationToken.None);
        var nextResult = await session.InteractAsync(
            new LiveMetasploitRequest { Control = "read", WaitSeconds = 1 },
            CancellationToken.None);

        Assert.StartsWith(head, result.Output);
        Assert.EndsWith(tail, result.Output);
        Assert.Contains("middle console output omitted", result.Output);
        Assert.True(result.OutputTruncated);
        Assert.True(result.OmittedCharacters > 0);
        Assert.True(result.CommandComplete);
        Assert.False(result.HasMore);
        Assert.True(result.Output.Length <= 10 * 1024);
        Assert.Equal("", nextResult.Output);
        Assert.False(nextResult.OutputTruncated);
        process.Kill();
        await process.WaitForExitAsync();
    }

    [Fact]
    public async Task InteractAsync_SessionWriteRoutesMeterpreterCommandOutsideConsole()
    {
        using var process = StartSleepingProcess();
        using var rpcClient = new ScriptedMetasploitRpcClient(Response("", "msf6 > ", false))
        {
            SessionListResponse = SessionListResponse("1", "meterpreter", "root @ target"),
            SessionReadResponses = new Queue<Dictionary<string, object?>>(new[]
            {
                SessionData("uid=0(root) gid=0(root)\n"),
                SessionData("")
            })
        };
        using var session = new LiveMetasploitSession(process, rpcClient, "1", _ => { });

        var result = await session.InteractAsync(
            new LiveMetasploitRequest
            {
                Control = "session_write",
                SessionId = "1",
                Input = "execute -f /usr/bin/id -c",
                WaitSeconds = 2
            },
            CancellationToken.None);

        Assert.Equal("uid=0(root) gid=0(root)\n", result.Output);
        Assert.Equal("meterpreter", result.InteractionMode);
        Assert.Equal("1", result.ActiveSessionId);
        Assert.Equal(0, rpcClient.WriteCount);
        Assert.Equal(1, rpcClient.MeterpreterWriteCount);
        Assert.Equal(0, rpcClient.ShellWriteCount);
        Assert.Single(result.Sessions);
        process.Kill();
        await process.WaitForExitAsync();
    }

    [Fact]
    public async Task InteractAsync_SessionStopUsesSessionRpcAndReturnsToConsoleMode()
    {
        using var process = StartSleepingProcess();
        using var rpcClient = new ScriptedMetasploitRpcClient(Response("", "msf6 > ", false))
        {
            SessionListResponse = SessionListResponse("2", "shell", "root shell")
        };
        using var session = new LiveMetasploitSession(process, rpcClient, "1", _ => { });

        await session.InteractAsync(
            new LiveMetasploitRequest
            {
                Control = "session_write",
                SessionId = "2",
                Input = "id",
                WaitSeconds = 1
            },
            CancellationToken.None);
        var result = await session.InteractAsync(
            new LiveMetasploitRequest { Control = "session_stop", SessionId = "2" },
            CancellationToken.None);

        Assert.Equal(1, rpcClient.StopSessionCount);
        Assert.Equal("console", result.InteractionMode);
        Assert.Empty(result.Sessions);
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
    public void DecodeResponse_ConvertsIntegerSessionMapKeysToStrings()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(1);
        writer.WriteMapHeader(2);
        writer.Write("type");
        writer.Write("meterpreter");
        writer.Write("info");
        writer.Write("root @ target");
        writer.Flush();

        var response = MetasploitRpcClient.DecodeResponse(buffer.WrittenMemory);

        var session = Assert.IsType<Dictionary<string, object?>>(response["1"]);
        Assert.Equal("meterpreter", MetasploitRpcClient.GetString(session, "type"));
        Assert.Equal("root @ target", MetasploitRpcClient.GetString(session, "info"));
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

    private static Dictionary<string, object?> SessionData(string data) =>
        new() { ["data"] = data };

    private static Dictionary<string, object?> SessionListResponse(string id, string type, string info) =>
        new()
        {
            [id] = new Dictionary<string, object?>
            {
                ["type"] = type,
                ["info"] = info,
                ["session_host"] = "172.30.50.10",
                ["tunnel_local"] = "172.30.50.1:4444",
                ["tunnel_peer"] = "172.30.50.10:45500"
            }
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
        public int MeterpreterWriteCount { get; private set; }
        public int ShellWriteCount { get; private set; }
        public int StopSessionCount { get; private set; }
        public Dictionary<string, object?> SessionListResponse { get; set; } = new();
        public Queue<Dictionary<string, object?>> SessionReadResponses { get; set; } = new();

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

        public Task<Dictionary<string, object?>> ListSessionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SessionListResponse);

        public Task<Dictionary<string, object?>> ReadMeterpreterSessionAsync(
            string sessionId,
            CancellationToken cancellationToken) => Task.FromResult(ReadSessionResponse());

        public Task WriteMeterpreterSessionAsync(
            string sessionId,
            string input,
            CancellationToken cancellationToken)
        {
            MeterpreterWriteCount++;
            return Task.CompletedTask;
        }

        public Task<Dictionary<string, object?>> ReadShellSessionAsync(
            string sessionId,
            CancellationToken cancellationToken) => Task.FromResult(ReadSessionResponse());

        public Task WriteShellSessionAsync(
            string sessionId,
            string input,
            CancellationToken cancellationToken)
        {
            ShellWriteCount++;
            return Task.CompletedTask;
        }

        public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            StopSessionCount++;
            SessionListResponse.Remove(sessionId);
            return Task.CompletedTask;
        }

        private Dictionary<string, object?> ReadSessionResponse() =>
            SessionReadResponses.Count > 0 ? SessionReadResponses.Dequeue() : SessionData("");

        public Task DetachSessionAsync(string consoleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InterruptSessionAsync(string consoleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DestroyConsoleAsync(string consoleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
