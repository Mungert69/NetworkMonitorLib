using NetworkMonitor.Objects;

namespace NetworkMonitor.Utils;
public class SwapMonitorPingInfoComparer : IEqualityComparer<SwapMonitorPingInfo>
{
    public bool Equals(SwapMonitorPingInfo? x, SwapMonitorPingInfo? y)
    {
        // Check if both objects are not null and have the same ID
        return x != null && y != null && x.ID == y.ID;
    }

    public int GetHashCode(SwapMonitorPingInfo obj)
    {
        // Return the hash code based on the ID property
        return obj.ID.GetHashCode();
    }
}