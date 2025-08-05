using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
namespace NetworkMonitor.Connection;
internal sealed class DuplexStream : NetworkStream
{
    private readonly Stream _readSide;
    private readonly Stream _writeSide;

    public DuplexStream(Stream readSide, Stream writeSide)
        : base(new Socket(SocketType.Stream, ProtocolType.Tcp))   // dummy socket
    {
        _readSide  = readSide;
        _writeSide = writeSide;
    }

    public override bool CanRead  => _readSide.CanRead;
    public override bool CanWrite => _writeSide.CanWrite;

    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken ct) =>
        _readSide.ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override Task WriteAsync(
        byte[] buffer, int offset, int count, CancellationToken ct) =>
        _writeSide.WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
}
