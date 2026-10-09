namespace NetworkMonitor.Connection;

/// <summary>Optional cycle integration; it must leave unrelated connects unchanged.</summary>
public interface IConnectCycleLifecycle
{
    void BeginCycle(IEnumerable<INetConnect> connects);
    bool TryPrepareConnect(INetConnect connect);
    void CompleteCycle();
    void Stop();
}
