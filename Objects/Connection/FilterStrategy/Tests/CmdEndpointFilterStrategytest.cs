using System;
using System.Collections.Generic;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

public class CmdEndpointFilterStrategyTest
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
    public void ShouldInclude_CorrectlyCyclesThroughEndpoints()
    {
        // Arrange: 6 endpoints, n=3, offset=0
        int n = 3;
        var strategy = new CmdEndpointFilterStrategy(n);
        var endpoints = new List<INetConnect>();
        for (int i = 0; i < 6; i++)
        {
            endpoints.Add(new TestNetConnect
            {
                MpiStatic = new MPIStatic { EndPointType = "nmap", MonitorIPID = i }
            });
        }
        strategy.SetTotalEndpoints(endpoints);

        // The first call to ShouldInclude should return true for the first endpoint, then false for the next two, then true, etc.
        var results = new List<bool>();
        foreach (var ep in endpoints)
            results.Add(strategy.ShouldInclude(ep));
        // Should be: true, false, false, true, false, false
        Assert.Equal(new List<bool> { true, false, false, true, false, false }, results);

        // After 6 calls, _counter should be reset and _offset incremented to 1
        // Next round: offset=1, so pattern is false, true, false, false, true, false
        results.Clear();
        foreach (var ep in endpoints)
            results.Add(strategy.ShouldInclude(ep));
        Assert.Equal(new List<bool> { false, true, false, false, true, false }, results);

        // After another 6 calls, _offset should be 2
        results.Clear();
        foreach (var ep in endpoints)
            results.Add(strategy.ShouldInclude(ep));
        Assert.Equal(new List<bool> { false, false, true, false, false, true }, results);
    }

    [Fact]
    public void ShouldInclude_ReturnsTrue_ForNonMatchingType()
    {
        var strategy = new CmdEndpointFilterStrategy(3);
        var netConnect = new TestNetConnect
        {
            MpiStatic = new MPIStatic { EndPointType = "other", MonitorIPID = 1 }
        };
        Assert.True(strategy.ShouldInclude(netConnect));
    }
}
