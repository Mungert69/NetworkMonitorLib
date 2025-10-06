using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkMonitor.Connection;

public class FilterStrategyConfig
{
    private List<string> _endpointTypeContains = new();
    private FilterIntervalConfig _fireInterval = new();

    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// List of partial endpoint type matches that should trigger this strategy.
    /// When empty we fall back to matching the strategy name.
    /// </summary>
    public List<string> EndpointTypeContains
    {
        get => _endpointTypeContains;
        set => _endpointTypeContains = value ?? new List<string>();
    }

    /// <summary>
    /// Declarative configuration describing how often a matching endpoint should fire.
    /// </summary>
    public FilterIntervalConfig FireInterval
    {
        get => _fireInterval;
        set => _fireInterval = value ?? new FilterIntervalConfig();
    }

    /// <summary>
    /// Optional randomisation settings for counter-based strategies.
    /// </summary>
    public RandomizationConfig? Randomization { get; set; }

    /// <summary>
    /// Legacy binding shim – keeps existing appsettings working.
    /// </summary>
    public int FilterSkip
    {
        get => FireInterval.Every;
        set => FireInterval.Every = value;
    }

    /// <summary>
    /// Legacy binding shim – keeps existing appsettings working.
    /// </summary>
    public int FilterStart
    {
        get => FireInterval.Offset;
        set => FireInterval.Offset = value;
    }

    // Optional: for JSON config binding, use a string and parse to TimeSpan
    public string? TimeSpanString { get; set; }

    public TimeSpan? TimeSpan
    {
        get
        {
            if (TimeSpanString == null) return null;
            if (System.TimeSpan.TryParse(TimeSpanString, out var ts))
            {
                return ts;
            }
            return null;
        }
    }

    public bool IsMatch(INetConnect netConnect)
    {
        if (netConnect?.MpiStatic == null) return false;

        var endpointType = netConnect.MpiStatic.EndPointType ?? string.Empty;
        var patterns = EndpointTypeContains ?? new List<string>();

        if (patterns.Count == 0)
        {
            return endpointType.IndexOf(StrategyName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return patterns.Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern) &&
            endpointType.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public override string ToString()
    {
        var targets = (EndpointTypeContains == null || EndpointTypeContains.Count == 0)
            ? StrategyName
            : string.Join(", ", EndpointTypeContains);

        var intervalDescription = FireInterval.ModeNormalized switch
        {
            FilterIntervalConfig.CounterMode =>
                $"every {FireInterval.EveryNormalized} (offset {FireInterval.OffsetNormalized})",
            FilterIntervalConfig.RandomizedCounterMode =>
                $"every {FireInterval.EveryNormalized} with random p={Randomization?.ProbabilityNormalized:F2}",
            FilterIntervalConfig.DailySlotMode =>
                $"daily slots={FireInterval.SlotsPerDayNormalized}",
            _ => "custom"
        };

        return $"{StrategyName} -> [{targets}] {FireInterval.ModeNormalized}: {intervalDescription}";
    }
}

public class FilterIntervalConfig
{
    public const string CounterMode = "counter";
    public const string RandomizedCounterMode = "randomized-counter";
    public const string DailySlotMode = "daily-slot";

    private string _mode = CounterMode;

    public string Mode
    {
        get => _mode;
        set => _mode = string.IsNullOrWhiteSpace(value)
            ? CounterMode
            : value.Trim().ToLowerInvariant();
    }

    public string ModeNormalized => Mode;

    public int Every { get; set; } = 1;

    public int Offset { get; set; }

    public int SlotsPerDay { get; set; } = 24;

    public int EveryNormalized => Every <= 0 ? 1 : Every;

    public int OffsetNormalized
    {
        get
        {
            var every = EveryNormalized;
            if (every == 0) return 0;
            var value = Offset % every;
            if (value < 0) value += every;
            return value;
        }
    }

    public int SlotsPerDayNormalized => SlotsPerDay <= 0 ? 24 : SlotsPerDay;

    public bool IsCounterBased =>
        ModeNormalized == CounterMode ||
        ModeNormalized == RandomizedCounterMode;
}

public class RandomizationConfig
{
    private double _probability = 0.5;

    public double Probability
    {
        get => _probability;
        set => _probability = double.IsNaN(value) ? 0.5 : value;
    }

    public double ProbabilityNormalized
    {
        get
        {
            if (_probability < 0d) return 0d;
            if (_probability > 1d) return 1d;
            return _probability;
        }
    }
}
