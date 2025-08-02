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

                int monitorIPID = netConnect.MpiStatic.MonitorIPID;
                bool hasLastRun = _lastConnectTimes.TryGetValue(monitorIPID, out var lastRun);

                Console.WriteLine(
                    $"[DailyEndpointFilterStrategy] MonitorIPID={monitorIPID}, EndPointType={netConnect.MpiStatic.EndPointType}, " +
                    $"assignedSlot={assignedSlot}, currentSlot={currentSlot}, now={now}, " +
                    $"hasLastRun={hasLastRun}, lastRun={lastRun}");

                // Only allow connect in the assigned slot
                if (assignedSlot != currentSlot)
                {
                    Console.WriteLine($"[DailyEndpointFilterStrategy] MonitorIPID={monitorIPID}: Not in assigned slot (assignedSlot={assignedSlot}, currentSlot={currentSlot})");
                    return false;
                }

                if (!hasLastRun || lastRun.Date < now.Date)
                {
                    // Mark as run for today
                    _lastConnectTimes[monitorIPID] = now;
                    Console.WriteLine($"[DailyEndpointFilterStrategy] MonitorIPID={monitorIPID}: Allowing run (first run today or never run before)");
                    return true;
                }

                // Already run today
                Console.WriteLine($"[DailyEndpointFilterStrategy] MonitorIPID={monitorIPID}: Already run today (lastRun={lastRun})");
                return false;
            }
            // If not a matching connect type, always include
            Console.WriteLine($"[DailyEndpointFilterStrategy] MonitorIPID={netConnect.MpiStatic.MonitorIPID}: Not a matching connect type ({netConnect.MpiStatic.EndPointType})");
            return true;
        }
    }
}
