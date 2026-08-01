using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NetworkMonitor.Connection;

/// <summary>
/// A configurable filter strategy that drives inclusion logic from configuration.
/// It supports counter, randomized-counter and daily slot based intervals.
/// </summary>
public class ConfigurableEndpointFilterStrategy : INetConnectFilterStrategy, IEndpointSettingStrategy
{
    private readonly List<FilterStrategyConfig> _configs;
    private readonly Dictionary<string, CounterState> _counterStates;
    private readonly ConcurrentDictionary<int, DateTime> _lastDailyRuns = new();
    private readonly ConcurrentDictionary<int, object> _dailyLocks = new();
    private readonly ConcurrentDictionary<int, HostSkipState> _hostSkipStates = new();
    private static readonly ThreadLocal<Random> _random = new(() => new Random());

    public ConfigurableEndpointFilterStrategy(IEnumerable<FilterStrategyConfig> configs)
    {
        _configs = (configs ?? Enumerable.Empty<FilterStrategyConfig>())
            .Select(NormalizeConfig)
            .ToList();

        _counterStates = _configs
            .Where(c => c.FireInterval.IsCounterBased)
            .ToDictionary(c => c.StrategyName, c => new CounterState(c.FireInterval.OffsetNormalized), StringComparer.OrdinalIgnoreCase);
    }

    public bool ShouldInclude(INetConnect netConnect)
    {
        if (HasHostSchedulingOverride(netConnect))
        {
            return EvaluateHostSkipCycles(netConnect);
        }

        var matchingConfigs = _configs.Where(c => c.IsMatch(netConnect)).ToList();
        if (matchingConfigs.Count == 0) return true;

        foreach (var config in matchingConfigs)
        {
            var include = config.FireInterval.ModeNormalized switch
            {
                FilterIntervalConfig.CounterMode => EvaluateCounter(config),
                FilterIntervalConfig.RandomizedCounterMode => EvaluateRandomizedCounter(config),
                FilterIntervalConfig.DailySlotMode => EvaluateDailySlot(config, netConnect),
                _ => true
            };

            if (!include)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasHostSchedulingOverride(INetConnect netConnect) =>
        netConnect?.MpiStatic?.SkipCycles.HasValue == true;

    private bool EvaluateHostSkipCycles(INetConnect netConnect)
    {
        var skipCycles = netConnect.MpiStatic.SkipCycles!.Value;
        if (skipCycles <= 0)
        {
            return true;
        }

        var monitorIpId = netConnect.MpiStatic.MonitorIPID;
        var state = _hostSkipStates.GetOrAdd(monitorIpId, _ => new HostSkipState(skipCycles));
        lock (state.SyncRoot)
        {
            // A host setting can be changed while the processor is running. Reset its cycle so
            // the new value applies immediately rather than inheriting stale skip state.
            if (state.SkipCycles != skipCycles)
            {
                state.SkipCycles = skipCycles;
                state.RemainingSkips = skipCycles;
                return true;
            }

            if (state.RemainingSkips <= 0)
            {
                state.RemainingSkips = skipCycles;
                return true;
            }

            state.RemainingSkips--;
            return false;
        }
    }

    public void SetTotalEndpoints(List<INetConnect> netConnects)
    {
        if (netConnects == null) return;

        foreach (var config in _configs.Where(c => c.FireInterval.IsCounterBased))
        {
            if (!_counterStates.TryGetValue(config.StrategyName, out var state)) continue;

            var matches = netConnects.Count(config.IsMatch);
            lock (state.SyncRoot)
            {
                state.TotalEndpoints = Math.Max(1, matches);
                if (state.Counter >= state.TotalEndpoints)
                {
                    state.Counter %= state.TotalEndpoints;
                }
            }
        }
    }

    private static FilterStrategyConfig NormalizeConfig(FilterStrategyConfig config)
    {
        config.EndpointTypeContains ??= new List<string>();
        config.FireInterval ??= new FilterIntervalConfig();

        if (string.IsNullOrWhiteSpace(config.StrategyName))
        {
            config.StrategyName = Guid.NewGuid().ToString("N");
        }

        if (config.FireInterval.IsCounterBased && config.Randomization == null &&
            config.FireInterval.ModeNormalized == FilterIntervalConfig.RandomizedCounterMode)
        {
            config.Randomization = new RandomizationConfig();
        }

        return config;
    }

    private bool EvaluateCounter(FilterStrategyConfig config)
    {
        var state = _counterStates[config.StrategyName];
        lock (state.SyncRoot)
        {
            var include = ((state.Counter + state.Offset) % config.FireInterval.EveryNormalized) == 0;
            AdvanceCounter(state, config.FireInterval.EveryNormalized);
            return include;
        }
    }

    private bool EvaluateRandomizedCounter(FilterStrategyConfig config)
    {
        var state = _counterStates[config.StrategyName];
        lock (state.SyncRoot)
        {
            var include = ((state.Counter + state.Offset) % config.FireInterval.EveryNormalized) == 0;
            AdvanceCounter(state, config.FireInterval.EveryNormalized);
            if (!include) return false;
        }

        var probability = config.Randomization?.ProbabilityNormalized ?? 0.5d;
        return _random.Value!.NextDouble() <= probability;
    }

    private bool EvaluateDailySlot(FilterStrategyConfig config, INetConnect netConnect)
    {
        if (netConnect?.MpiStatic == null) return false;

        var now = DateTime.UtcNow;
        var slots = config.FireInterval.SlotsPerDayNormalized;
        var minutesPerSlot = 1440d / slots;
        var assignedSlot = Math.Abs(netConnect.MpiStatic.MonitorIPID.ToString().GetHashCode()) % slots;
        var currentSlot = (int)(now.TimeOfDay.TotalMinutes / minutesPerSlot);

        if (currentSlot != assignedSlot)
        {
            return false;
        }

        var lockObj = _dailyLocks.GetOrAdd(netConnect.MpiStatic.MonitorIPID, _ => new object());
        lock (lockObj)
        {
            var hasLastRun = _lastDailyRuns.TryGetValue(netConnect.MpiStatic.MonitorIPID, out var lastRun);
            if (!hasLastRun || lastRun.Date < now.Date)
            {
                _lastDailyRuns[netConnect.MpiStatic.MonitorIPID] = now;
                return true;
            }

            return false;
        }
    }

    private static void AdvanceCounter(CounterState state, int every)
    {
        state.Counter++;
        if (state.Counter >= state.TotalEndpoints)
        {
            state.Counter = 0;
            state.Offset = (state.Offset + 1) % every;
        }
    }

    private sealed class CounterState
    {
        public CounterState(int offset)
        {
            Offset = offset;
        }

        public int Counter { get; set; }
        public int Offset { get; set; }
        public int TotalEndpoints { get; set; } = 1;
        public object SyncRoot { get; } = new();
    }

    private sealed class HostSkipState
    {
        public HostSkipState(int skipCycles)
        {
            SkipCycles = skipCycles;
            // A host-level override applies immediately. It then skips the configured
            // number of following scheduler cycles.
            RemainingSkips = 0;
        }

        public int SkipCycles { get; set; }
        public int RemainingSkips { get; set; }
        public object SyncRoot { get; } = new();
    }
}
