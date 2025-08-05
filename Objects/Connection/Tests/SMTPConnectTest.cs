using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

/* ─────────  mini helper for duplex in-memory stream  ───────── */
internal sealed class DuplexStream : Stream
{
    private readonly Stream _read;
    private readonly Stream _write;

    public DuplexStream(Stream readSide, Stream writeSide)
    {
        _read  = readSide;
        _write = writeSide;
    }

    public override bool CanRead  => _read.CanRead;
    public override bool CanWrite => _write.CanWrite;
    public override bool CanSeek  => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        _read.Read(buffer, offset, count);

    public override void Write(byte[] buffer, int offset, int count) =>
        _write.Write(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _read.ReadAsync(buffer, offset, count, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _write.WriteAsync(buffer, offset, count, cancellationToken);

    public override void Flush() => _write.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _write.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/* ─────────  spy class that overrides all seams  ───────── */
internal sealed class SmtpConnectSpy : SMTPConnect
{
    private readonly Func<Stream> _streamFactory;
    public MemoryStream ClientToServer { get; } = new();

    public SmtpConnectSpy(string serverGreeting)
    {
        var serverToClient = new MemoryStream(Encoding.ASCII.GetBytes(serverGreeting));
        if (string.IsNullOrEmpty(serverGreeting))
        {
            serverToClient.Close(); // force closed stream to trigger exception on read
        }
        _streamFactory = () => new DuplexStream(serverToClient, ClientToServer);
    }

    protected override TcpClient CreateTcpClient() => new();                 // no real socket
    protected override Stream CreateStream(TcpClient _) => _streamFactory();

    protected override ValueTask ConnectAsync(                               // short-circuit connect
        TcpClient _, string __, int ___, CancellationToken ____)
        => ValueTask.CompletedTask;
}

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class SmtpConnectTests
    {
        [Fact]
        public async Task TestConnectionAsync_returns_success_on_220()
        {
            var sut = new SmtpConnectSpy("220 test.local ready\r\n")
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
            Assert.StartsWith("Unexpected response", result.Message);
            Assert.Contains("500", result.Data);
        }

        [Fact]
        public async Task TestConnectionAsync_bubbles_exception_when_stream_closed()
        {
            var sut = new SmtpConnectSpy(string.Empty)   // zero-byte banner
            {
                MpiStatic = new MPIStatic { Address = "ignored" }
            };

            await Assert.ThrowsAnyAsync<Exception>(() => sut.TestConnectionAsync(25));
        }
    }
}
