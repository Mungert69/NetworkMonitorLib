using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Connection;

/// <summary>Validates the final result even when generated code bypasses helpers.</summary>
internal sealed class GuardedDynamicConnect(NetConnect inner, DynamicConnectStatusPolicy policy) : INetConnect
{
    public EndpointMeasurementMetadata Measurement => inner.Measurement;
    public IReadOnlyCollection<EndpointMeasurementMetadata> MeasurementVariants => inner.MeasurementVariants;
    public ushort RoundTrip { get => inner.RoundTrip; set => inner.RoundTrip = value; }
    public uint PiID { get => inner.PiID; set => inner.PiID = value; }
    public bool IsLongRunning { get => inner.IsLongRunning; set => inner.IsLongRunning = value; }
    public bool IsRunning { get => inner.IsRunning; set => inner.IsRunning = value; }
    public bool IsQueued { get => inner.IsQueued; set => inner.IsQueued = value; }
    public bool IsEnabled { get => inner.IsEnabled; set => inner.IsEnabled = value; }
    public MPIConnect MpiConnect { get => inner.MpiConnect; set => inner.MpiConnect = value; }
    public MPIStatic MpiStatic { get => inner.MpiStatic; set => inner.MpiStatic = value; }
    public CancellationTokenSource Cts { get => inner.Cts; set => inner.Cts = value; }
    public void PreConnect() => inner.PreConnect();
    public void PostConnect() => inner.PostConnect();
    public async Task Connect()
    {
        try { await inner.Connect().ConfigureAwait(false); }
        finally { policy.Validate(inner.MpiConnect); }
    }
}
