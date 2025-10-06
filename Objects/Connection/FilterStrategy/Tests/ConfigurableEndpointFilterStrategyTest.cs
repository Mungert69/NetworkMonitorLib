using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

public class ConfigurableEndpointFilterStrategyTest
{
    [Fact]
    public void CounterStrategy_IncludesEveryNthEndpoint()
    {
        var config = new FilterStrategyConfig
        {
            StrategyName = "nmap",
            EndpointTypeContains = new List<string> { "nmap" },
            FireInterval = new FilterIntervalConfig
            {
                Mode = FilterIntervalConfig.CounterMode,
                Every = 2,
                Offset = 0
            }
        };

        var strategy = new ConfigurableEndpointFilterStrategy(new[] { config });
        var netConnects = Enumerable.Range(0, 4)
            .Select(i => new TestNetConnect(i, "nmap"))
            .Cast<INetConnect>()
            .ToList();

        strategy.SetTotalEndpoints(netConnects);

        var results = netConnects.Select(strategy.ShouldInclude).ToList();

        Assert.True(results[0]);
        Assert.False(results[1]);
        Assert.True(results[2]);
        Assert.False(results[3]);
    }

    [Fact]
    public void CounterStrategy_HonoursOffset()
    {
        var config = new FilterStrategyConfig
        {
            StrategyName = "smtp",
            EndpointTypeContains = new List<string> { "smtp" },
            FireInterval = new FilterIntervalConfig
            {
                Mode = FilterIntervalConfig.CounterMode,
                Every = 3,
                Offset = 1
            }
        };

        var strategy = new ConfigurableEndpointFilterStrategy(new[] { config });
        var netConnects = Enumerable.Range(0, 3)
            .Select(i => new TestNetConnect(i, "smtp"))
            .Cast<INetConnect>()
            .ToList();

        strategy.SetTotalEndpoints(netConnects);

        var results = netConnects.Select(strategy.ShouldInclude).ToList();

        Assert.False(results[0]);
        Assert.True(results[1]);
        Assert.False(results[2]);
    }

    [Fact]
    public void RandomizedCounterStrategy_UsesProbability()
    {
        var config = new FilterStrategyConfig
        {
            StrategyName = "crawl",
            EndpointTypeContains = new List<string> { "crawl" },
            FireInterval = new FilterIntervalConfig
            {
                Mode = FilterIntervalConfig.RandomizedCounterMode,
                Every = 1,
                Offset = 0
            },
            Randomization = new RandomizationConfig { Probability = 1.0 }
        };

        var strategy = new ConfigurableEndpointFilterStrategy(new[] { config });
        var netConnect = new TestNetConnect(1, "crawl");

        strategy.SetTotalEndpoints(new List<INetConnect> { netConnect });

        Assert.True(strategy.ShouldInclude(netConnect));

        config.Randomization.Probability = 0.0;
        var strategyZero = new ConfigurableEndpointFilterStrategy(new[] { config });
        strategyZero.SetTotalEndpoints(new List<INetConnect> { netConnect });
        Assert.False(strategyZero.ShouldInclude(netConnect));
    }

    [Fact]
    public void DailySlotStrategy_AllowsOncePerDay()
    {
        var config = new FilterStrategyConfig
        {
            StrategyName = "daily",
            EndpointTypeContains = new List<string> { "daily" },
            FireInterval = new FilterIntervalConfig
            {
                Mode = FilterIntervalConfig.DailySlotMode,
                SlotsPerDay = 1
            }
        };

        var strategy = new ConfigurableEndpointFilterStrategy(new[] { config });
        var netConnect = new TestNetConnect(1, "daily");

        Assert.True(strategy.ShouldInclude(netConnect));
        Assert.False(strategy.ShouldInclude(netConnect));
    }

    [Fact]
    public void NonMatchingEndpoints_BypassFilters()
    {
        var config = new FilterStrategyConfig
        {
            StrategyName = "nmap",
            EndpointTypeContains = new List<string> { "nmap" },
            FireInterval = new FilterIntervalConfig
            {
                Mode = FilterIntervalConfig.CounterMode,
                Every = 10
            }
        };

        var strategy = new ConfigurableEndpointFilterStrategy(new[] { config });
        var netConnect = new TestNetConnect(1, "smtp");

        Assert.True(strategy.ShouldInclude(netConnect));
    }

    private sealed class TestNetConnect : INetConnect
    {
        public TestNetConnect(int id, string endPointType, bool enabled = true)
        {
            MpiStatic = new MPIStatic
            {
                MonitorIPID = id,
                EndPointType = endPointType,
                Enabled = enabled
            };
            IsEnabled = enabled;
        }

        public ushort RoundTrip { get; set; }
        public uint PiID { get; set; }
        public bool IsLongRunning { get; set; }
        public bool IsRunning { get; set; }
        public bool IsQueued { get; set; }
        public bool IsEnabled { get; set; }
        public MPIConnect MpiConnect { get; set; } = new();
        public MPIStatic MpiStatic { get; set; }
        public CancellationTokenSource Cts { get; set; } = new();
        public Task Connect() => Task.CompletedTask;
        public void PostConnect() { }
        public void PreConnect() { }
    }
}
