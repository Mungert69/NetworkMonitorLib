using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkMonitor.Connection
{
    public class DailyEndpointFilterStrategy : INetConnectFilterStrategy, IEndpointSettingStrategy
    {
        private int _totalEndpoints;
        private List<string> _matchingConnectNames = new List<string>() { "daily" };

        // Holds last connect time for each MonitorIPID
        private readonly Dictionary<int, DateTime> _lastConnectTimes = new();

        public void SetTotalEndpoints(List<INetConnect> netConnects)
        {
            _totalEndpoints = netConnects.Count(x =>
                _matchingConnectNames.Any(connectName => x.MpiStatic.EndPointType.ToLower().Contains(connectName))
            );
        }

        public bool ShouldInclude(INetConnect netConnect)
        {
            if (_matchingConnectNames.Any(connectName => netConnect.MpiStatic.EndPointType.ToLower().Contains(connectName)))
            {
                var now = DateTime.UtcNow;

                // Spread connects by hour of day (24 slots)
                int slotsPerDay = 24;
                int assignedSlot = Math.Abs(netConnect.MpiStatic.MonitorIPID.ToString().GetHashCode()) % slotsPerDay;
                int currentSlot = now.Hour;

                // Only allow connect in the assigned slot
                if (assignedSlot != currentSlot)
                    return false;

                int monitorIPID = netConnect.MpiStatic.MonitorIPID;
                if (!_lastConnectTimes.TryGetValue(monitorIPID, out var lastRun) || lastRun.Date < now.Date)
                {
                    // Mark as run for today
                    _lastConnectTimes[monitorIPID] = now;
                    return true;
                }

                // Already run today
                return false;
            }
            // If not a matching connect type, always include
            return true;
        }
    }
}
