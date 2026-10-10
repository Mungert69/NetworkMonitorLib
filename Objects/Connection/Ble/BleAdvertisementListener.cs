using System.Diagnostics;

namespace NetworkMonitor.Connection;

/// <summary>A received advertisement. Packet bytes are copied on entry and never exposed mutably.</summary>
public sealed class BleAdvertisement
{
    private readonly byte[] _bytes;
    public string Address { get; }
    public long Timestamp { get; }
    public long Sequence { get; }
    public DateTime ReceivedUtc { get; }
    internal BleAdvertisement(string address, byte[] bytes, long timestamp, long sequence, DateTime receivedUtc)
    {
        Address = address;
        _bytes = bytes.ToArray();
        Timestamp = timestamp;
        Sequence = sequence;
        ReceivedUtc = receivedUtc;
    }
    public byte[] CopyBytes() => _bytes.ToArray();
    internal bool HasSamePayload(ReadOnlySpan<byte> bytes) => _bytes.AsSpan().SequenceEqual(bytes);
}

/// <summary>One immutable view, retained safely by asynchronous connects after later publication/eviction.</summary>
public sealed class BleAdvertisementSnapshot
{
    public long Timestamp { get; }
    public DateTime CapturedUtc { get; }
    public long LastSequence { get; }
    public string Error { get; }
    public IReadOnlyList<BleAdvertisement> Packets { get; }
    internal BleAdvertisementSnapshot(long timestamp, DateTime capturedUtc, long sequence,
        IEnumerable<BleAdvertisement> packets, string error)
    {
        Timestamp = timestamp;
        CapturedUtc = capturedUtc;
        LastSequence = sequence;
        Packets = Array.AsReadOnly(packets.OrderBy(p => p.Sequence).ToArray());
        Error = error;
    }
    public IEnumerable<BleAdvertisement> Window(string address, TimeSpan window) =>
        Packets.Where(p => p.Address == BleAdvertisementListener.NormalizeAddress(address)
            && p.Timestamp <= Timestamp
            && (Timestamp - p.Timestamp) / (double)Stopwatch.Frequency <= window.TotalSeconds);
}

/// <summary>Shared contracts also support command processors compiled from source at runtime.</summary>
public interface IBleAdvertisementListenerConsumer
{
    IBleAdvertisementListener? Listener { get; set; }
}
public interface IBleBroadcastSnapshotProcessor : IBleAdvertisementListenerConsumer
{
    NetworkMonitor.Objects.ResultObj ReadSnapshot(string arguments, BleAdvertisementSnapshot? snapshot,
        TimeSpan window, CancellationToken cancellationToken);
}
public interface IBleListenSnapshotProcessor : IBleAdvertisementListenerConsumer
{
    NetworkMonitor.Objects.ResultObj ReadSnapshot(string arguments, BleAdvertisementSnapshot? snapshot,
        long afterSequence, CancellationToken cancellationToken);
}

public interface IBleAdvertisementSource : IDisposable
{
    void Start(Action<string, byte[]> receive, Action<string> failed);
    void Stop();
}

/// <summary>One processor instance's capture history. The application owns its lifetime.</summary>
public interface IBleAdvertisementListener : IDisposable
{
    BleAdvertisementSnapshot Snapshot { get; }
    void Configure(IEnumerable<(string Address, TimeSpan Window)> windows, bool enabled);
    void CompleteCycle();
}

/// <summary>Single scanner and live store. Only cycle-end cleanup evicts; there are no capacity limits.</summary>
public sealed class BleAdvertisementListener : IBleAdvertisementListener
{
    private readonly object _gate = new();
    // Platform start/stop may invoke receive callbacks, so do not hold the packet lock around them.
    private readonly object _lifecycleGate = new();
    private readonly IBleAdvertisementSource _source;
    private readonly Func<long> _clock;
    private readonly Dictionary<string, List<BleAdvertisement>> _packets = new();
    private Dictionary<string, TimeSpan> _retention = new();
    private BleAdvertisementSnapshot _snapshot;
    private long _sequence;
    private bool _started;
    private bool _acceptingPackets;
    private long _sourceGeneration;
    private bool _disposed;
    private bool _restartRequested;
    private string _error = "";

    public BleAdvertisementListener(IBleAdvertisementSource source, Func<long>? clock = null)
    {
        _source = source;
        _clock = clock ?? Stopwatch.GetTimestamp;
        _snapshot = new(_clock(), DateTime.UtcNow, 0, Array.Empty<BleAdvertisement>(), "");
    }
    public BleAdvertisementSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public static string NormalizeAddress(string address) => address.Trim().Replace("-", "").Replace(":", "").ToUpperInvariant();

    /// <summary>Refresh from ALL enabled BLE connects, including ones skipped by this cycle's schedule.</summary>
    public void Configure(IEnumerable<(string Address, TimeSpan Window)> windows, bool enabled)
    {
        lock (_lifecycleGate) ConfigureCore(windows, enabled);
    }
    private void ConfigureCore(IEnumerable<(string Address, TimeSpan Window)> windows, bool enabled)
    {
        if (_disposed) return;
        var rules = windows.GroupBy(w => NormalizeAddress(w.Address)).ToDictionary(g => g.Key,
            g => TimeSpan.FromTicks(checked(g.Max(w => w.Window.Ticks) * 2)));
        lock (_gate) _retention = rules;
        // Called only by the processor lifecycle/cycle, never from receive callbacks.
        if (!enabled)
        {
            lock (_gate) { _acceptingPackets = false; ++_sourceGeneration; }
            _source.Stop();
            _started = false;
            lock (_gate)
            {
                _packets.Clear();
                _error = "";
                Volatile.Write(ref _snapshot, new(_clock(), DateTime.UtcNow, _sequence,
                    Array.Empty<BleAdvertisement>(), ""));
            }
            return;
        }
        bool restart;
        lock (_gate) { restart = _restartRequested; _restartRequested = false; }
        if (restart) { _source.Stop(); _started = false; }
        if (_started) return;
        try
        {
            long generation;
            lock (_gate) { _error = ""; _acceptingPackets = true; generation = ++_sourceGeneration; }
            _source.Start((address, bytes) => Receive(generation, address, bytes), error =>
            {
                lock (_gate)
                {
                    if (!_acceptingPackets || generation != _sourceGeneration) return;
                    _error = error;
                    _restartRequested = true;
                }
            });
            _started = true;
        }
        catch (Exception ex)
        {
            _source.Stop();
            lock (_gate) { _error = ex.Message; _acceptingPackets = false; }
        }
    }
    private void Receive(long generation, string address, byte[] bytes)
    {
        lock (_gate)
        {
            if (_disposed || !_acceptingPackets || generation != _sourceGeneration) return;
            address = NormalizeAddress(address);
            if (!_packets.TryGetValue(address, out var packets)) _packets[address] = packets = new();
            long timestamp = _clock();
            // Compare with the last retained reception: suppressed repeats must not
            // indefinitely postpone an unchanged advertiser's next sample.
            if (packets.Count > 0)
            {
                var last = packets[^1];
                if (timestamp >= last.Timestamp && timestamp - last.Timestamp < Stopwatch.Frequency
                    && last.HasSamePayload(bytes)) return;
            }
            packets.Add(new(address, bytes, timestamp, ++_sequence, DateTime.UtcNow));
        }
    }
    /// <summary>Snapshot BEFORE cleanup. Receive writes cannot fall between snapshot and eviction.</summary>
    public void CompleteCycle()
    {
        lock (_gate)
        {
            long now = _clock();
            var next = new BleAdvertisementSnapshot(now, DateTime.UtcNow, _sequence,
                _packets.Values.SelectMany(p => p), _error);
            Volatile.Write(ref _snapshot, next);
            foreach (var address in _packets.Keys.ToArray())
            {
                if (!_retention.TryGetValue(address, out var age)) _packets.Remove(address);
                else
                {
                    _packets[address].RemoveAll(p => (now - p.Timestamp) / (double)Stopwatch.Frequency > age.TotalSeconds);
                    if (_packets[address].Count == 0) _packets.Remove(address);
                }
            }
        }
    }
    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            lock (_gate) { if (_disposed) return; _disposed = true; _acceptingPackets = false; _packets.Clear(); }
            _source.Dispose();
        }
    }
}

/// <summary>Extracts an AD payload without interpreting manufacturer/device data.</summary>
public static class BleAdvertisementPayload
{
    public static byte[] Extract(byte[] raw, string mode, int manufacturer, string serviceUuid, out string type)
    {
        type = mode;
        if (mode == "raw") return raw;
        Guid? desired = Guid.TryParse(serviceUuid, out var uuid) ? uuid : null;
        if (desired == null && ushort.TryParse(serviceUuid.Replace("0x", ""),
            System.Globalization.NumberStyles.HexNumber, null, out var shortUuid))
            desired = BluetoothUuid(shortUuid);
        for (int i = 0; i < raw.Length;)
        {
            int length = raw[i++];
            if (length == 0) break;
            if (i + length > raw.Length) break;
            byte adType = raw[i];
            var data = raw.AsSpan(i + 1, length - 1);
            if (mode == "manufacturer" && adType == 0xff && data.Length >= 2
                && (manufacturer < 0 || (data[0] | data[1] << 8) == manufacturer)) return data.ToArray();
            int prefix = adType == 0x16 ? 2 : adType == 0x20 ? 4 : adType == 0x21 ? 16 : 0;
            if (mode == "service" && prefix > 0 && data.Length >= prefix)
            {
                Guid found = prefix == 16 ? new Guid(data[..16]) : BluetoothUuid(prefix == 2
                    ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data)
                    : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data));
                if (desired == null || desired == found) return data[prefix..].ToArray();
            }
            i += length;
        }
        if (manufacturer >= 0 || !string.IsNullOrWhiteSpace(serviceUuid)) return Array.Empty<byte>();
        // Preserve the previous raw fallback for decoders that can inspect complete advertisements.
        type = "raw";
        return raw;
    }
    private static Guid BluetoothUuid(uint id) => new($"{id:X8}-0000-1000-8000-00805f9b34fb");
}
