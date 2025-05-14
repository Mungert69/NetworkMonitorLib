using System.Collections.Generic;
using NetworkMonitor.Objects;
namespace NetworkMonitor.Utils;
public class MonitorStatusAlertComparer : IEqualityComparer<MonitorStatusAlert>
{
    public bool Equals(MonitorStatusAlert? x, MonitorStatusAlert? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;

        return x.ID == y.ID
               && x.UserID == y.UserID
               && x.Address == y.Address
               && x.UserName == y.UserName
               && x.AppID == y.AppID
               && x.EndPointType == y.EndPointType
               && x.Timeout == y.Timeout
               && x.AddUserEmail == y.AddUserEmail
               && x.IsEmailVerified == y.IsEmailVerified
               && x.AlertFlag == y.AlertFlag
               && x.AlertSent == y.AlertSent
               && x.DownCount == y.DownCount
               && x.EventTime == y.EventTime
               && x.IsUp == y.IsUp
               && x.Message == y.Message
               && x.MonitorPingInfoID == y.MonitorPingInfoID;
    }

    public int GetHashCode(MonitorStatusAlert obj)
    {
        if (ReferenceEquals(obj, null)) return 0;

        int hash = obj.ID;
        hash = (hash * 397) ^ (obj.UserID != null ? obj.UserID.GetHashCode() : 0);
        hash = (hash * 397) ^ (obj.Address != null ? obj.Address.GetHashCode() : 0);
        hash = (hash * 397) ^ (obj.UserName != null ? obj.UserName.GetHashCode() : 0);
        hash = (hash * 397) ^ (obj.AppID != null ? obj.AppID.GetHashCode() : 0);
        hash = (hash * 397) ^ (obj.EndPointType != null ? obj.EndPointType.GetHashCode() : 0);
        hash = (hash * 397) ^ obj.Timeout;
        hash = (hash * 397) ^ (obj.AddUserEmail != null ? obj.AddUserEmail.GetHashCode() : 0);
        hash = (hash * 397) ^ obj.IsEmailVerified.GetHashCode();
        hash = (hash * 397) ^ obj.AlertFlag.GetHashCode();
        hash = (hash * 397) ^ obj.AlertSent.GetHashCode();
        hash = (hash * 397) ^ obj.DownCount;
        hash = (hash * 397) ^ (obj.EventTime != null ? obj.EventTime.GetHashCode() : 0);
        hash = (hash * 397) ^ (obj.IsUp.HasValue ? obj.IsUp.Value.GetHashCode() : 0);
        hash = (hash * 397) ^ (obj.Message != null ? obj.Message.GetHashCode() : 0);
        hash = (hash * 397) ^ obj.MonitorPingInfoID;
        return hash;
    }
}
