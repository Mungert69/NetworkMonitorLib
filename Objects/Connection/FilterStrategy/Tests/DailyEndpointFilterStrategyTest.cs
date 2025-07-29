using System;
using System.Collections.Generic;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

public class DailyEndpointFilterStrategyTest
{
    private class TestNetConnect : INetConnect
    {
        public ushort RoundTrip { get; set; }
        public uint PiID { get; set; }
        public bool IsLongRunning { get; set; }
        public bool IsRunning { get; set; }
        public bool IsQueued { get; set; }
        public bool IsEnabled { get; set; }
        public MPIConnect MpiConnect { get; set; }
        public MPIStatic MpiStatic { get; set; }
        public CancellationTokenSource Cts { get; set; }
        public DateTime? LastConnectTime { get; set; }
        public Task Connect() => Task.CompletedTask;
        public void PostConnect() { }
        public void PreConnect() { }
    }

    [Fact]
    public void ShouldInclude_ReturnsTrue_AndThenFalse_WhenCalledTwiceInAssignedSlot()
    {
        var strategy = new DailyEndpointFilterStrategy();
        var now = DateTime.UtcNow;
        var slotsPerDay = 24;
        int currentSlot = now.Hour;
        int monitorIPID = 1;
        while ((Math.Abs(monitorIPID.ToString().GetHashCode()) % slotsPerDay) != currentSlot)
            monitorIPID++;

        var netConnect = new TestNetConnect
        {
            MpiStatic = new MPIStatic { EndPointType = "daily", MonitorIPID = monitorIPID }
        };
        // First call: should be included (never run)
        Assert.True(strategy.ShouldInclude(netConnect));
        // Second call: should NOT be included (already run today)
        Assert.False(strategy.ShouldInclude(netConnect));
    }

    [Fact]
    public void ShouldInclude_ReturnsTrue_AfterDayChanges()
    {
        var strategy = new DailyEndpointFilterStrategy();
        var now = DateTime.UtcNow;
        var slotsPerDay = 24;
        int currentSlot = now.Hour;
        int monitorIPID = 1;
        while ((Math.Abs(monitorIPID.ToString().GetHashCode()) % slotsPerDay) != currentSlot)
            monitorIPID++;

        var netConnect = new TestNetConnect
        {
            MpiStatic = new MPIStatic { EndPointType = "daily", MonitorIPID = monitorIPID }
        };
        // First call: should be included
        Assert.True(strategy.ShouldInclude(netConnect));
        // Simulate next day by updating the internal dictionary
        var field = typeof(DailyEndpointFilterStrategy).GetField("_lastConnectTimes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        var dict = field.GetValue(strategy) as Dictionary<int, DateTime>;
        Assert.NotNull(dict);
        dict[monitorIPID] = now.AddDays(-1);
        // Should be included again (since last run was yesterday)
        Assert.True(strategy.ShouldInclude(netConnect));
    }

    [Fact]
    public void ShouldInclude_ReturnsFalse_WhenNotInAssignedSlot()
    {
        var strategy = new DailyEndpointFilterStrategy();
        var now = DateTime.UtcNow;
        var slotsPerDay = 24;
        int currentSlot = now.Hour;
        int monitorIPID = 1;
        while ((Math.Abs(monitorIPID.ToString().GetHashCode()) % slotsPerDay) == currentSlot)
            monitorIPID++;

        var netConnect = new TestNetConnect
        {
            MpiStatic = new MPIStatic { EndPointType = "daily", MonitorIPID = monitorIPID }
        };
        Assert.False(strategy.ShouldInclude(netConnect));
    }

    [Fact]
    public void ShouldInclude_ReturnsTrue_ForNonMatchingType()
    {
        var strategy = new DailyEndpointFilterStrategy();
        var netConnect = new TestNetConnect
        {
            MpiStatic = new MPIStatic { EndPointType = "other", MonitorIPID = 1 }
        };
        Assert.True(strategy.ShouldInclude(netConnect));
    }
}
