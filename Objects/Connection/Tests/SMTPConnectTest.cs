using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

/* ─────────  in-memory SMTP conversation harness  ───────── */
internal sealed class FakeSmtpStream : Stream
{
    private readonly Queue<byte[]> _responses = new();
    private MemoryStream? _current;
    private readonly MemoryStream _clientCapture = new();
    private readonly StringBuilder _commandBuffer = new();
    private readonly Func<string, string?> _onCommand;

    public FakeSmtpStream(IEnumerable<string> initialResponses, Func<string, string?> onCommand)
    {
        foreach (var response in initialResponses)
        {
            if (!string.IsNullOrEmpty(response))
            {
                _responses.Enqueue(Encoding.ASCII.GetBytes(response));
            }
        }
        _onCommand = onCommand;
    }

    public MemoryStream ClientWrites => _clientCapture;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    private bool EnsureCurrentResponse()
    {
        while (_current == null || _current.Position >= _current.Length)
        {
            if (_responses.Count == 0)
            {
                _current = null;
                return false;
            }
            _current = new MemoryStream(_responses.Dequeue(), writable: false);
        }
        return true;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (!EnsureCurrentResponse()) return 0;
        var read = _current!.Read(buffer, offset, count);
        if (read == 0 && EnsureCurrentResponse())
        {
            read = _current!.Read(buffer, offset, count);
        }
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Task.FromResult(Read(buffer, offset, count));

    public override void Write(byte[] buffer, int offset, int count)
    {
        _clientCapture.Write(buffer, offset, count);

        _commandBuffer.Append(Encoding.ASCII.GetString(buffer, offset, count));
        while (true)
        {
            var text = _commandBuffer.ToString();
            var terminator = text.IndexOf("\n", StringComparison.Ordinal);
            if (terminator < 0) break;

            var line = text.Substring(0, terminator + 1);
            _commandBuffer.Remove(0, terminator + 1);

            var response = _onCommand?.Invoke(line.Trim());
            if (!string.IsNullOrEmpty(response))
            {
                _responses.Enqueue(Encoding.ASCII.GetBytes(response));
            }
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/* ─────────  spy class that overrides network seams  ───────── */
internal sealed class SmtpConnectSpy : SMTPConnect
{
    private readonly FakeSmtpStream _stream;

    public SmtpConnectSpy(string banner, string? heloResponse = null)
    {
        _stream = new FakeSmtpStream(
            new[] { banner },
            command =>
            {
                if (command.StartsWith("HELO", StringComparison.OrdinalIgnoreCase) && heloResponse != null)
                {
                    return heloResponse;
                }
                if (command.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    return "221 Bye\r\n";
                }
                return null;
            });
    }

    public MemoryStream ClientToServer => _stream.ClientWrites;

    protected override TcpClient CreateTcpClient() => new();
    protected override Stream CreateStream(TcpClient _) => _stream;
    protected override ValueTask ConnectAsync(TcpClient _, string __, int ___, CancellationToken ____) => ValueTask.CompletedTask;
}

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class SmtpConnectTests
    {
        [Fact]
        public async Task TestConnectionAsync_returns_success_on_220()
        {
            var sut = new SmtpConnectSpy("220 test.local ready\r\n", "250 test.local greets\r\n")
            {
                MpiStatic = new MPIStatic { Address = "ignored" }
            };

            var result = await sut.TestConnectionAsync(25);

            Assert.True(result.Success);
            Assert.Equal("Connect HELO", result.Message);

            var sent = Encoding.ASCII.GetString(sut.ClientToServer.ToArray());
            Assert.StartsWith("HELO", sent);
        }

        [Fact]
        public async Task TestConnectionAsync_returns_failure_on_non_220()
        {
            var sut = new SmtpConnectSpy("500 oops\r\n")
            {
                MpiStatic = new MPIStatic { Address = "ignored" }
            };

            var result = await sut.TestConnectionAsync(25);

            Assert.False(result.Success);
            Assert.StartsWith("Invalid banner", result.Message);
            Assert.Contains("500", result.Data);
        }

        [Fact]
        public async Task TestConnectionAsync_handles_empty_banner()
        {
            var sut = new SmtpConnectSpy(string.Empty)   // zero-byte banner
            {
                MpiStatic = new MPIStatic { Address = "ignored" }
            };

            var result = await sut.TestConnectionAsync(25);

            Assert.False(result.Success);
            Assert.StartsWith("Invalid banner", result.Message);
        }
    }
}
