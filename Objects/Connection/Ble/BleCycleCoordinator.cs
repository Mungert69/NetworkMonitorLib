namespace NetworkMonitor.Connection;

public sealed record BleCaptureRequirement(string Address, TimeSpan Window);

/// <summary>Only connects consuming BLE snapshots participate in this lifecycle.</summary>
public interface IBleCycleParticipant
{
    BleCaptureRequirement? CaptureRequirement { get; }
    void PrepareSnapshot(BleAdvertisementSnapshot snapshot);
}

/// <summary>BLE policy at the processor-cycle boundary; does not own the injected listener.</summary>
public sealed class BleCycleCoordinator(IBleAdvertisementListener listener) : IConnectCycleLifecycle
{
    public void BeginCycle(IEnumerable<INetConnect> connects)
    {
        var participants = connects.Where(c => c.IsEnabled && c.MpiStatic.Enabled)
            .OfType<IBleCycleParticipant>().ToArray();
        var requirements = participants.Select(c => c.CaptureRequirement)
            .OfType<BleCaptureRequirement>().Select(r => (r.Address, r.Window));
        listener.Configure(requirements, participants.Length > 0);
    }

    public bool TryPrepareConnect(INetConnect connect)
    {
        if (connect is not IBleCycleParticipant participant) return true;
        // Preserve the in-flight operation's snapshot, token and identifiers.
        if (connect.IsRunning || connect.IsQueued) return false;
        participant.PrepareSnapshot(listener.Snapshot);
        return true;
    }

    public void CompleteCycle() => listener.CompleteCycle();
    public void Stop() => listener.Configure(Array.Empty<(string, TimeSpan)>(), false);
}
