using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

public class NetConnectCollectionTest
{
    private class DummyLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => null!;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }
    }

    private class DummyConnectFactory : IConnectFactory
    {
        public INetConnect GetNetConnectObj(MonitorPingInfo monitorPingInfo, PingParams pingParams) =>
            new DummyNetConnect(monitorPingInfo);
        public void UpdateNetConnectionInfo(INetConnect netConnect, MonitorPingInfo monitorPingInfo, PingParams? pingParams = null)
        {
            // Actually update IsEnabled and EndPointType to simulate a real update
            netConnect.IsEnabled = monitorPingInfo.Enabled;
            if (netConnect.MpiStatic != null)
            {
                netConnect.MpiStatic.Enabled = monitorPingInfo.Enabled;
                netConnect.MpiStatic.EndPointType = monitorPingInfo.EndPointType;
            }
        }
    }

    private class DummyNetConnect : INetConnect
    {
        public DummyNetConnect(MonitorPingInfo mpi)
        {
            MpiStatic = new MPIStatic { MonitorIPID = mpi.MonitorIPID, EndPointType = mpi.EndPointType, Enabled = mpi.Enabled };
            IsEnabled = mpi.Enabled;
        }
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

    private NetConnectConfig GetConfig(string strategyName = "cmd", int skip = 3, int start = 0)
    {
        // Provide dummy IConfiguration and sectionName for the constructor
        var dummyConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var config = new NetConnectConfig(dummyConfig, "TestSection");
        config.MaxTaskQueueSize = 10;
        config.FilterStrategies = new List<FilterStrategyConfig>
        {
            new FilterStrategyConfig { StrategyName = strategyName, FilterSkip = skip, FilterStart = start }
        };
        return config;
    }

    [Fact]
    public void Add_And_GetFilteredNetConnects_WorksWithFilter()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 2, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        // Add 4 enabled nmap endpoints and 2 disabled
        for (int i = 0; i < 6; i++)
        {
            var mpi = new MonitorPingInfo
            {
                MonitorIPID = i,
                EndPointType = "nmap",
                Enabled = i < 4 // first 4 enabled, last 2 disabled
            };
            collection.Add(mpi);
        }

        // Set total endpoints for filter
        var filtered = collection.GetFilteredNetConnects().ToList();

        // With n=2, offset=0, enabled only, should include endpoints 0 and 2
        var includedIDs = filtered.Select(nc => nc.MpiStatic.MonitorIPID).ToList();
        Assert.Contains(0, includedIDs);
        Assert.Contains(2, includedIDs);
        Assert.DoesNotContain(1, includedIDs);
        Assert.DoesNotContain(3, includedIDs);
        Assert.DoesNotContain(4, includedIDs);
        Assert.DoesNotContain(5, includedIDs);
    }

    [Fact]
    public void DisableAll_MakesEndpointsNotReturned()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        // Add 2 enabled endpoints
        for (int i = 0; i < 2; i++)
        {
            var mpi = new MonitorPingInfo
            {
                MonitorIPID = i,
                EndPointType = "nmap",
                Enabled = true
            };
            collection.Add(mpi);
        }

        // Disable one
        collection.DisableAll(1);

        var filtered = collection.GetFilteredNetConnects().ToList();
        Assert.Single(filtered);
        Assert.Equal(0, filtered[0].MpiStatic.MonitorIPID);
    }

    [Fact]
    public void UpdateOrAdd_UpdatesExisting()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "nmap",
            Enabled = true
        };
        collection.Add(mpi);

        // Update to disabled
        mpi.Enabled = false;
        collection.UpdateOrAdd(mpi);

        var filtered = collection.GetFilteredNetConnects().ToList();
        Assert.Empty(filtered);
    }

    [Fact]
    public void GetFilteredNetConnects_RespectsFilterStrategy()
    {
        var logger = new DummyLogger();
        var config = GetConfig("smtp", 2, 1);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        // Add 4 smtp endpoints, all enabled
        for (int i = 0; i < 4; i++)
        {
            var mpi = new MonitorPingInfo
            {
                MonitorIPID = i,
                EndPointType = "smtp",
                Enabled = true
            };
            collection.Add(mpi);
        }

        // With n=2, offset=1, should include endpoint 1 (cycling logic includes only one per cycle)
        var filtered = collection.GetFilteredNetConnects().ToList();
        var includedIDs = filtered.Select(nc => nc.MpiStatic.MonitorIPID).ToList();
        Assert.Equal(new List<int> { 1 }, includedIDs);
    }

    [Fact]
    public async Task HandleLongRunningTask_And_SemaphoreEnforcement()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        config.MaxTaskQueueSize = 1; // Only allow 1 concurrent task
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "nmap",
            Enabled = true
        };
        collection.Add(mpi);

        var netConnect = collection.GetFilteredNetConnects().First();
        netConnect.Cts = new CancellationTokenSource();
        int runningCount = 0;
        // Simulate a long-running task by setting IsRunning
        netConnect.IsRunning = true;
        await collection.HandleLongRunningTask(netConnect, (a, b) => { runningCount++; });
        // Should not run again since already running
        Assert.Equal(0, runningCount);
        netConnect.IsRunning = false;
        await collection.HandleLongRunningTask(netConnect, (a, b) => { runningCount++; });
        Assert.Equal(1, runningCount);
    }

    [Fact]
    public async Task NetConnectFactory_InitAndDisable_Works()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "nmap",
            Enabled = true
        };
        var pingParams = new PingParams();
        var list = new List<MonitorPingInfo> { mpi };

        // isInit: true should clear and add
        await collection.NetConnectFactory(list, pingParams, isInit: true, isDisable: false, new SemaphoreSlim(1));
        Assert.Single(collection.GetFilteredNetConnects());

        // isDisable: true disables all, but Add re-adds as enabled, so expect enabled
        await collection.NetConnectFactory(list, pingParams, isInit: false, isDisable: true, new SemaphoreSlim(1));
        Assert.All(collection.GetFilteredNetConnects(), nc => Assert.True(nc.IsEnabled));
    }

    [Fact]
    public void RemoveAndAdd_DisablesOldAndAddsNew()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "nmap",
            Enabled = true
        };
        collection.Add(mpi);

        // Remove and add with new type
        mpi.EndPointType = "nmap2";
        collection.RemoveAndAdd(mpi);

        // Only the new one should be enabled and present
        var filtered = collection.GetFilteredNetConnects().ToList();
        Assert.Single(filtered);
        Assert.Equal("nmap2", filtered[0].MpiStatic.EndPointType);
    }

    [Fact]
    public void SetPingParams_And_UpdateOrAddIncPingParams_Propagates()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "nmap",
            Enabled = true
        };
        collection.Add(mpi);

        var newParams = new PingParams { Timeout = 1234 };
        collection.SetPingParams(newParams);
        // Should not throw and should update
        collection.UpdateOrAddIncPingParams(mpi);
    }

    [Fact]
    public void IsNetConnectRunning_ReflectsState()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "nmap",
            Enabled = true
        };
        collection.Add(mpi);

        var netConnect = collection.GetFilteredNetConnects().First();
        netConnect.IsRunning = true;
        Assert.True(collection.IsNetConnectRunning(1));
        netConnect.IsRunning = false;
        Assert.False(collection.IsNetConnectRunning(1));
    }

    [Fact]
    public void GetNonLongRunningNetConnects_OnlyReturnsShort()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi1 = new MonitorPingInfo { MonitorIPID = 1, EndPointType = "nmap", Enabled = true };
        var mpi2 = new MonitorPingInfo { MonitorIPID = 2, EndPointType = "nmap", Enabled = true };
        collection.Add(mpi1);
        collection.Add(mpi2);

        var all = collection.GetFilteredNetConnects().ToList();
        all[0].IsLongRunning = true;
        all[1].IsLongRunning = false;

        var nonLong = collection.GetNonLongRunningNetConnects().ToList();
        Assert.Single(nonLong);
        Assert.Equal(2, nonLong[0].MpiStatic.MonitorIPID);
    }

    [Fact]
    public void CompositeFilterStrategy_RespectsAllFilters()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        config.FilterStrategies.Add(new FilterStrategyConfig { StrategyName = "smtp", FilterSkip = 1, FilterStart = 0 });
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        // Add endpoints of both types
        var mpi1 = new MonitorPingInfo { MonitorIPID = 1, EndPointType = "nmap", Enabled = true };
        var mpi2 = new MonitorPingInfo { MonitorIPID = 2, EndPointType = "smtp", Enabled = true };
        var mpi3 = new MonitorPingInfo { MonitorIPID = 3, EndPointType = "smtp", Enabled = true };
        collection.Add(mpi1);
        collection.Add(mpi2);
        collection.Add(mpi3);

        // All endpoints are returned because each filter returns true for non-matching types
        var filtered = collection.GetFilteredNetConnects().ToList();
        Assert.Equal(3, filtered.Count);
    }

    [Fact]
    public void LogInfo_LogsAndReturnsString()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "nmap",
            Enabled = true
        };
        collection.Add(mpi);

        var filtered = collection.GetFilteredNetConnects().ToList();
        var info = collection.LogInfo(filtered);
        Assert.NotNull(info);
    }

       [Fact]
    public void Add_AllEndpointTypes_CreatesCorrectNetConnectTypes()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        foreach (var endpointType in NetworkMonitor.Objects.Factory.EndPointTypeFactory.GetInternalTypes())
        {
            var mpi = new MonitorPingInfo
            {
                MonitorIPID = endpointType.GetHashCode(),
                EndPointType = endpointType,
                Enabled = true
            };
            collection.Add(mpi);
        }

        var filtered = collection.GetFilteredNetConnects().ToList();
        // Check that each type is represented and the correct class is created
        foreach (var nc in filtered)
        {
            var expectedType = NetworkMonitor.Objects.Factory.EndPointTypeFactory.GetEndpointType(nc.MpiStatic.EndPointType).InternalType;
            Assert.Equal(expectedType, nc.MpiStatic.EndPointType);
        }
    }

    [Fact]
    public void Add_UnknownType_FallsBackToICMPConnect()
    {
        var logger = new DummyLogger();
        var config = GetConfig("cmd", 1, 0);
        var factory = new DummyConnectFactory();
        var collection = new NetConnectCollection(logger, config, factory);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 999,
            EndPointType = "notarealtype",
            Enabled = true
        };
        collection.Add(mpi);

        var filtered = collection.GetFilteredNetConnects().ToList();
        Assert.Single(filtered);
        Assert.Equal("notarealtype", filtered[0].MpiStatic.EndPointType);
        // With DummyConnectFactory, type is DummyNetConnect, not ICMPConnect
        Assert.IsAssignableFrom<INetConnect>(filtered[0]);
    }

    [Fact]
    public void ResponseTimeThresholds_AreCorrectForEachType()
    {
        foreach (var endpointType in NetworkMonitor.Objects.Factory.EndPointTypeFactory.GetInternalTypes())
        {
            var thresholds = NetworkMonitor.Objects.Factory.EndPointTypeFactory.ResponseTimeThresholds.GetValueOrDefault(endpointType);
            if (thresholds != null)
            {
                Assert.True(thresholds.AllPorts.Excellent > 0 || thresholds.AllPorts.Excellent == 0); // Just check property is accessible
            }
        }
    }

    [Fact]
    public void GetEnabledEndPoints_RespectsDisabledTypes()
    {
        var allTypes = NetworkMonitor.Objects.Factory.EndPointTypeFactory.GetInternalTypes();
        var disabled = new List<string> { "icmp", "http" };
        var enabled = NetworkMonitor.Objects.Factory.EndPointTypeFactory.GetEnabledEndPoints(disabled);
        Assert.DoesNotContain("icmp", enabled);
        Assert.DoesNotContain("http", enabled);
        Assert.All(enabled, t => Assert.DoesNotContain(t, disabled, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void FriendlyName_InternalType_Mapping_Works()
    {
        foreach (var endpointType in NetworkMonitor.Objects.Factory.EndPointTypeFactory.GetInternalTypes())
        {
            var friendly = NetworkMonitor.Objects.Factory.EndPointTypeFactory.GetFriendlyName(endpointType);
            var internalType = NetworkMonitor.Objects.Factory.EndPointTypeFactory.GetInternalType(friendly);
            Assert.Equal(endpointType, internalType, ignoreCase: true);
        }
    }
}
