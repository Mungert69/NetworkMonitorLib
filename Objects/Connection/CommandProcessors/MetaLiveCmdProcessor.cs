using MessagePack;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Connection;

internal sealed class LiveMetasploitRequest
{
    [JsonPropertyName("input")]
    public string Input { get; set; } = "";

    [JsonPropertyName("control")]
    public string Control { get; set; } = "write";

    [JsonPropertyName("wait_seconds")]
    public int WaitSeconds { get; set; } = 60;
}

internal sealed class LiveMetasploitResponse
{
    public string Output { get; set; } = "";
    public string Prompt { get; set; } = "";
    public bool Busy { get; set; }
    public bool Closed { get; set; }
    public bool HasMore { get; set; }
    public string? Error { get; set; }
}

internal interface IMetasploitRpcClient : IDisposable
{
    Task LoginAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, object?>> CreateConsoleAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, object?>> ReadConsoleAsync(string consoleId, CancellationToken cancellationToken);
    Task WriteConsoleAsync(string consoleId, string input, CancellationToken cancellationToken);
    Task DetachSessionAsync(string consoleId, CancellationToken cancellationToken);
    Task InterruptSessionAsync(string consoleId, CancellationToken cancellationToken);
    Task DestroyConsoleAsync(string consoleId, CancellationToken cancellationToken);
}

internal sealed class MetasploitRpcClient : IMetasploitRpcClient
{
    private readonly HttpClient _httpClient;
    private readonly string _username;
    private readonly string _password;
    private string _token = "";

    public MetasploitRpcClient(int port, string username, string password)
    {
        _username = username;
        _password = password;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        var response = await CallAsync("auth.login", false, cancellationToken, _username, _password);
        _token = GetString(response, "token");
        if (string.IsNullOrWhiteSpace(_token))
        {
            throw new InvalidOperationException("Metasploit RPC authentication did not return a token.");
        }
    }

    public Task<Dictionary<string, object?>> CreateConsoleAsync(CancellationToken cancellationToken) =>
        CallWithAuthenticationAsync("console.create", cancellationToken);

    public Task<Dictionary<string, object?>> ReadConsoleAsync(string consoleId, CancellationToken cancellationToken) =>
        CallWithAuthenticationAsync("console.read", cancellationToken, consoleId);

    public async Task WriteConsoleAsync(string consoleId, string input, CancellationToken cancellationToken) =>
        _ = await CallWithAuthenticationAsync("console.write", cancellationToken, consoleId, input);

    public async Task DetachSessionAsync(string consoleId, CancellationToken cancellationToken) =>
        _ = await CallWithAuthenticationAsync("console.session_detach", cancellationToken, consoleId);

    public async Task InterruptSessionAsync(string consoleId, CancellationToken cancellationToken) =>
        _ = await CallWithAuthenticationAsync("console.session_kill", cancellationToken, consoleId);

    public async Task DestroyConsoleAsync(string consoleId, CancellationToken cancellationToken) =>
        _ = await CallWithAuthenticationAsync("console.destroy", cancellationToken, consoleId);

    private async Task<Dictionary<string, object?>> CallWithAuthenticationAsync(
        string method,
        CancellationToken cancellationToken,
        params object?[] arguments)
    {
        if (string.IsNullOrEmpty(_token))
        {
            await LoginAsync(cancellationToken);
        }

        var response = await CallAsync(method, true, cancellationToken, arguments);
        if (HasAuthenticationError(response))
        {
            await LoginAsync(cancellationToken);
            response = await CallAsync(method, true, cancellationToken, arguments);
        }

        ThrowIfRpcError(response);
        return response;
    }

    private async Task<Dictionary<string, object?>> CallAsync(
        string method,
        bool authenticated,
        CancellationToken cancellationToken,
        params object?[] arguments)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteArrayHeader(arguments.Length + (authenticated ? 2 : 1));
        writer.Write(method);
        if (authenticated)
        {
            writer.Write(_token);
        }
        foreach (var argument in arguments)
        {
            WriteValue(ref writer, argument);
        }
        writer.Flush();

        using var content = new ByteArrayContent(buffer.WrittenSpan.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("binary/message-pack");
        using var httpResponse = await _httpClient.PostAsync("api", content, cancellationToken);
        httpResponse.EnsureSuccessStatusCode();
        var bytes = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var reader = new MessagePackReader(bytes);
        return ReadMap(ref reader);
    }

    private static void WriteValue(ref MessagePackWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNil();
                break;
            case string text:
                writer.Write(text);
                break;
            case int number:
                writer.Write(number);
                break;
            case bool flag:
                writer.Write(flag);
                break;
            default:
                writer.Write(value.ToString());
                break;
        }
    }

    private static Dictionary<string, object?> ReadMap(ref MessagePackReader reader)
    {
        var count = reader.ReadMapHeader();
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < count; index++)
        {
            var key = reader.ReadString() ?? "";
            result[key] = ReadValue(ref reader);
        }
        return result;
    }

    private static object? ReadValue(ref MessagePackReader reader)
    {
        return reader.NextMessagePackType switch
        {
            MessagePackType.Nil => ReadNil(ref reader),
            MessagePackType.Boolean => reader.ReadBoolean(),
            MessagePackType.Integer => reader.ReadInt64(),
            MessagePackType.Float => reader.ReadDouble(),
            MessagePackType.String => reader.ReadString(),
            MessagePackType.Binary => reader.ReadBytes()?.ToArray(),
            MessagePackType.Map => ReadMap(ref reader),
            MessagePackType.Array => ReadArray(ref reader),
            _ => throw new InvalidDataException($"Unsupported MessagePack type {reader.NextMessagePackType}.")
        };
    }

    private static object? ReadNil(ref MessagePackReader reader)
    {
        reader.ReadNil();
        return null;
    }

    private static List<object?> ReadArray(ref MessagePackReader reader)
    {
        var count = reader.ReadArrayHeader();
        var result = new List<object?>(count);
        for (var index = 0; index < count; index++)
        {
            result.Add(ReadValue(ref reader));
        }
        return result;
    }

    private static bool HasAuthenticationError(Dictionary<string, object?> response) =>
        GetString(response, "error").Contains("auth", StringComparison.OrdinalIgnoreCase)
        || GetString(response, "error_message").Contains("auth", StringComparison.OrdinalIgnoreCase);

    private static void ThrowIfRpcError(Dictionary<string, object?> response)
    {
        if (response.TryGetValue("error", out var error)
            && error != null
            && !string.Equals(error.ToString(), "false", StringComparison.OrdinalIgnoreCase))
        {
            var message = GetString(response, "error_message");
            throw new InvalidOperationException(
                $"Metasploit RPC error: {(string.IsNullOrEmpty(message) ? error : message)}");
        }
    }

    internal static string GetString(Dictionary<string, object?> response, string key) =>
        response.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";

    internal static bool GetBoolean(Dictionary<string, object?> response, string key) =>
        response.TryGetValue(key, out var value) && value is bool flag && flag;

    public void Dispose() => _httpClient.Dispose();
}

internal sealed class LiveMetasploitSession : IDisposable
{
    private const int MaxResponseCharacters = 32 * 1024;
    private const int MaxBufferedCharacters = 1024 * 1024;
    private readonly Process _process;
    private readonly IMetasploitRpcClient _rpcClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _pendingOutput = new();
    private string _prompt = "";
    private bool _busy;
    private bool _closed;

    public LiveMetasploitSession(
        Process process,
        IMetasploitRpcClient rpcClient,
        string consoleId,
        Action<LiveMetasploitSession> processExited)
    {
        _process = process;
        _rpcClient = rpcClient;
        ConsoleId = consoleId;
        _process.Exited += (_, _) => processExited(this);
    }

    public string ConsoleId { get; }
    public bool HasExited => _closed || _process.HasExited;

    public async Task<LiveMetasploitResponse> InteractAsync(
        LiveMetasploitRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (HasExited)
            {
                return new LiveMetasploitResponse { Closed = true, Error = "The live msfconsole process has exited." };
            }

            var control = request.Control.Trim().ToLowerInvariant();
            switch (control)
            {
                case "write":
                    if (!string.IsNullOrEmpty(request.Input))
                    {
                        var input = request.Input.EndsWith('\n') ? request.Input : request.Input + "\n";
                        await _rpcClient.WriteConsoleAsync(ConsoleId, input, cancellationToken);
                    }
                    break;
                case "read":
                    break;
                case "detach":
                    await _rpcClient.DetachSessionAsync(ConsoleId, cancellationToken);
                    break;
                case "interrupt":
                    await _rpcClient.InterruptSessionAsync(ConsoleId, cancellationToken);
                    break;
                default:
                    return new LiveMetasploitResponse { Error = $"Unsupported console control '{request.Control}'." };
            }

            var wait = TimeSpan.FromSeconds(Math.Clamp(request.WaitSeconds, 1, 60));
            var stopwatch = Stopwatch.StartNew();
            do
            {
                var read = await _rpcClient.ReadConsoleAsync(ConsoleId, cancellationToken);
                AppendOutput(MetasploitRpcClient.GetString(read, "data"));
                _prompt = MetasploitRpcClient.GetString(read, "prompt");
                _busy = MetasploitRpcClient.GetBoolean(read, "busy");
                if (!_busy)
                {
                    break;
                }
                await Task.Delay(250, cancellationToken);
            }
            while (stopwatch.Elapsed < wait);

            return BuildResponse();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LiveMetasploitResponse> CloseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_closed)
            {
                try
                {
                    await _rpcClient.DestroyConsoleAsync(ConsoleId, cancellationToken);
                }
                catch
                {
                    // The process is terminated below even when RPC cleanup cannot complete.
                }
                _closed = true;
                if (!_process.HasExited)
                {
                    try
                    {
                        await _process.StandardInput.WriteLineAsync("unload msgrpc; exit -y");
                        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await _process.WaitForExitAsync(exitTimeout.Token);
                    }
                    catch
                    {
                        if (!_process.HasExited)
                        {
                            _process.Kill(true);
                        }
                    }
                }
            }
            return new LiveMetasploitResponse { Closed = true, Prompt = _prompt };
        }
        finally
        {
            _gate.Release();
        }
    }

    private void AppendOutput(string output)
    {
        if (string.IsNullOrEmpty(output)) return;
        _pendingOutput.Append(output);
        if (_pendingOutput.Length > MaxBufferedCharacters)
        {
            var removeCount = _pendingOutput.Length - MaxBufferedCharacters;
            _pendingOutput.Remove(0, removeCount);
            _pendingOutput.Insert(0, "[Earlier console output discarded because the unread buffer exceeded 1 MiB.]\n");
            if (_pendingOutput.Length > MaxBufferedCharacters)
            {
                _pendingOutput.Length = MaxBufferedCharacters;
            }
        }
    }

    private LiveMetasploitResponse BuildResponse()
    {
        var take = Math.Min(MaxResponseCharacters, _pendingOutput.Length);
        var output = take == 0 ? "" : _pendingOutput.ToString(0, take);
        if (take > 0) _pendingOutput.Remove(0, take);
        return new LiveMetasploitResponse
        {
            Output = output,
            Prompt = _prompt,
            Busy = _busy,
            Closed = _closed,
            HasMore = _pendingOutput.Length > 0
        };
    }

    public void Dispose()
    {
        try
        {
            CloseAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            if (!_process.HasExited) _process.Kill(true);
        }
        _rpcClient.Dispose();
        _process.Dispose();
        _gate.Dispose();
    }
}

public sealed class MetaLiveCmdProcessor : CmdProcessor
{
    private readonly ConcurrentDictionary<string, LiveMetasploitSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _creationLocks = new();
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MetaLiveCmdProcessor(
        ILogger logger,
        ILocalCmdProcessorStates cmdProcessorStates,
        IRabbitRepo rabbitRepo,
        NetConnectConfig netConfig)
        : base(logger, cmdProcessorStates, rabbitRepo, netConfig, 2)
    {
    }

    public override async Task<ResultObj> RunCommand(
        string arguments,
        CancellationToken cancellationToken,
        ProcessorScanDataObj? processorScanDataObj = null)
    {
        try
        {
            var request = JsonSerializer.Deserialize<LiveMetasploitRequest>(arguments)
                ?? new LiveMetasploitRequest();
            var key = GetSessionKey(processorScanDataObj);
            LiveMetasploitResponse response;

            if (string.Equals(request.Control, "close", StringComparison.OrdinalIgnoreCase))
            {
                if (_sessions.TryRemove(key, out var existing))
                {
                    response = await existing.CloseAsync(cancellationToken);
                    existing.Dispose();
                }
                else
                {
                    response = new LiveMetasploitResponse { Closed = true };
                }
            }
            else
            {
                var session = await GetOrCreateSessionAsync(key, cancellationToken);
                response = await session.InteractAsync(request, cancellationToken);
                if (session.HasExited)
                {
                    _sessions.TryRemove(key, out _);
                    session.Dispose();
                }
            }

            return new ResultObj
            {
                Success = string.IsNullOrEmpty(response.Error),
                Message = JsonSerializer.Serialize(response, ResponseJsonOptions)
            };
        }
        catch (OperationCanceledException)
        {
            return new ResultObj
            {
                Success = false,
                Message = JsonSerializer.Serialize(new LiveMetasploitResponse
                {
                    Busy = true,
                    Error = "The console interaction was cancelled. The live console was left running."
                }, ResponseJsonOptions)
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Live Metasploit console interaction failed.");
            return new ResultObj
            {
                Success = false,
                Message = JsonSerializer.Serialize(new LiveMetasploitResponse
                {
                    Error = $"Live Metasploit console error: {exception.Message}"
                }, ResponseJsonOptions)
            };
        }
    }

    public override string GetCommandHelp() =>
        "Maintains one Metasploit RPC console per user and LLM session. Controls: write, read, detach, interrupt, close.";

    private async Task<LiveMetasploitSession> GetOrCreateSessionAsync(string key, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(key, out var existing) && !existing.HasExited)
        {
            return existing;
        }

        var creationLock = _creationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await creationLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(key, out existing) && !existing.HasExited)
            {
                return existing;
            }
            existing?.Dispose();
            var created = await StartSessionAsync(key, cancellationToken);
            _sessions[key] = created;
            if (created.HasExited && _sessions.TryRemove(
                    new KeyValuePair<string, LiveMetasploitSession>(key, created)))
            {
                created.Dispose();
                throw new InvalidOperationException("The live msfconsole process exited during session startup.");
            }
            return created;
        }
        finally
        {
            creationLock.Release();
        }
    }

    private async Task<LiveMetasploitSession> StartSessionAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var msfPath = await FindMsfconsoleAsync(cancellationToken);
        var port = GetAvailableLoopbackPort();
        const string username = "networkmonitor";
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        var isWindows = string.Equals(_netConfig.OSPlatform, "windows", StringComparison.OrdinalIgnoreCase);
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : msfPath,
                Arguments = isWindows ? $"/d /s /c \"\"{msfPath}\" -q\"" : "-q",
                WorkingDirectory = Path.GetDirectoryName(msfPath) ?? "",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.AutoFlush = true;
        await process.StandardInput.WriteLineAsync(
            $"load msgrpc ServerHost=127.0.0.1 ServerPort={port} User={username} Pass={password} SSL=false TokenTimeout=300");

        var rpcClient = new MetasploitRpcClient(port, username, password);
        try
        {
            var started = Stopwatch.StartNew();
            Exception? lastError = null;
            while (started.Elapsed < TimeSpan.FromSeconds(60))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    lastError = new InvalidOperationException("msfconsole exited while its RPC service was starting.");
                    break;
                }
                try
                {
                    await rpcClient.LoginAsync(cancellationToken);
                    var console = await rpcClient.CreateConsoleAsync(cancellationToken);
                    var consoleId = MetasploitRpcClient.GetString(console, "id");
                    if (string.IsNullOrEmpty(consoleId))
                    {
                        throw new InvalidOperationException("Metasploit RPC did not return a console ID.");
                    }
                    return new LiveMetasploitSession(
                        process,
                        rpcClient,
                        consoleId,
                        session => RemoveExitedSession(key, session));
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = exception;
                    await Task.Delay(500, cancellationToken);
                }
            }

            throw new InvalidOperationException("Timed out starting the loopback Metasploit RPC service.", lastError);
        }
        catch
        {
            rpcClient.Dispose();
            if (!process.HasExited) process.Kill(true);
            process.Dispose();
            throw;
        }
    }

    private async Task<string> FindMsfconsoleAsync(CancellationToken cancellationToken)
    {
        var isWindows = string.Equals(_netConfig.OSPlatform, "windows", StringComparison.OrdinalIgnoreCase);
        var locator = isWindows ? "where" : "which";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = locator,
                Arguments = "msfconsole",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var path = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new FileNotFoundException("Metasploit executable msfconsole was not found in PATH.");
        }
        return path.Trim();
    }

    private static int GetAvailableLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void RemoveExitedSession(string key, LiveMetasploitSession session)
    {
        if (_sessions.TryRemove(new KeyValuePair<string, LiveMetasploitSession>(key, session)))
        {
            _ = Task.Run(session.Dispose);
        }
    }

    private static string GetSessionKey(ProcessorScanDataObj? data)
    {
        var userId = data?.LlmServiceObj?.UserInfo?.UserID ?? "unknown-user";
        var sessionId = data?.LlmServiceObj?.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException(
                "A non-empty LLM SessionId is required for a live Metasploit console.");
        }
        return $"{userId}:{sessionId}";
    }

    public override void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();
        foreach (var creationLock in _creationLocks.Values)
        {
            creationLock.Dispose();
        }
        _creationLocks.Clear();
        base.Dispose();
    }
}
