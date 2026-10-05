using System.Globalization;
using System.Text.RegularExpressions;
namespace NetworkMonitor.Connection;

/// <summary>Wire-independent physical range. Derived from the same schema as decoding.</summary>
public sealed record BleMetricRange(string Metric, string Unit, double Minimum, double Maximum, double Resolution)
{
    public EndpointMeasurementMetadata Meaning { get; init; } = MeasurementAnalysisTemplates.Metric(Metric.Replace('_', ' '), Unit, "unspecified");
    public static BleMetricRange Field(string label, string unit, int width, bool signed, double scale = 1, double offset = 0, uint? na = null, string kind = "continuous")
    {
        long min = signed ? -(1L << (width - 1)) : 0;
        long max = signed ? (1L << (width - 1)) - 1 : (1L << width) - 1;
        long signedNa = na.HasValue && signed && (na.Value & (1u << (width - 1))) != 0 ? na.Value - (1L << width) : na ?? -1L;
        if (na.HasValue && signedNa == min) min++;
        if (na.HasValue && signedNa == max) max--;
        double a = min * scale + offset, b = max * scale + offset;
        return new(BleReading.MetricName(label), unit, Math.Min(a, b), Math.Max(a, b), Math.Abs(scale)) { Meaning = MeasurementAnalysisTemplates.Metric(label, unit, kind) };
    }
}
public sealed record BleMetricEncoding(string Format, string Metric, string Unit, double Scale, double Offset, double Minimum, double Maximum)
{
    public EndpointMeasurementMetadata Meaning { get; init; } = MeasurementAnalysisTemplates.Metric(Metric, Unit, "unspecified");
    public EndpointMeasurementMetadata Measurement => Meaning with { Unit = Unit, Scale = Scale, Offset = Offset, Type = Type };
    public string Type => $"v2:{Format}:{Metric}";
    public string Status => $"BLE {Type}";
    public double Decode(double sample) => sample * Scale + Offset;
    public bool TryEncode(double value, out ushort sample)
    {
        sample = 0;
        if (!double.IsFinite(value) || value < Minimum - Scale * 1e-6 || value > Maximum + Scale * 1e-6) return false;
        double number = Math.Round((value - Offset) / Scale, MidpointRounding.AwayFromZero);
        if (number < 0 || number > 65534) return false;
        sample = (ushort)number; return true;
    }
}
/// <summary>Versioned immutable numeric encodings; analysis wording can evolve independently.</summary>
public static class BleMetricCatalogue
{
    public const int MaxSample = 65534; // 65535 is the existing failed-ping marker.
    private static readonly Lazy<IReadOnlyDictionary<string, BleMetricEncoding>> Catalogue = new(Build);
    public static IReadOnlyCollection<BleMetricEncoding> Definitions => Catalogue.Value.Values.ToArray();
    public static BleMetricEncoding? Find(string format, string metric)
    {
        metric = BleMetricSelector.Canonical(metric);
        string key = $"v2:{format.ToLowerInvariant()}:{metric}";
        if (Catalogue.Value.TryGetValue(key, out var value)) return value;
        // Repeated BTHome objects use the same fixed encoding as their base name.
        if (format.Equals("bthome", StringComparison.OrdinalIgnoreCase))
            Catalogue.Value.TryGetValue($"v2:bthome:{Regex.Replace(metric, @"_\d+$", "")}", out value);
        return value == null ? null : value with { Metric = metric };
    }
    public static BleMetricEncoding? FromStatus(string? status)
    {
        if (status == null || !status.StartsWith("BLE v2:", StringComparison.Ordinal)) return null;
        var parts = status[7..].Split(':');
        return parts.Length == 2 ? Find(parts[0], parts[1]) : null;
    }
    public static string? FormatFromArgs(string? args)
    {
        var match = Regex.Match(args ?? "", @"(?:^|\s)--format(?:=|\s+)[""']?(?<format>[a-z0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["format"].Value.ToLowerInvariant() : null;
    }
    public static string? MetricFromArgs(string? args)
    {
        var match = Regex.Match(args ?? "", @"(?:^|\s)--metric(?:=|\s+)[""']?(?<metric>[a-z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? BleMetricSelector.Canonical(match.Groups["metric"].Value) : null;
    }
    private static IReadOnlyDictionary<string, BleMetricEncoding> Build()
    {
        var entries = new Dictionary<string, BleMetricEncoding>(StringComparer.Ordinal);
        var ranges = new Dictionary<string, IEnumerable<BleMetricRange>> {
            ["victron"] = VictronDeviceRecordDecoders.CreateDefaults().SelectMany(d => d.Metrics),
            ["ruuvi"] = RuuviPayloadDecoder.Metrics,
            ["bthome"] = BTHomePayloadDecoder.Metrics
        };
        foreach (var (format, specs) in ranges)
        foreach (var group in specs.GroupBy(r => r.Metric))
        {
            if (group.Select(r => r.Unit).Distinct().Count() != 1) throw new InvalidOperationException($"Ambiguous BLE units for {format}:{group.Key}");
            double min = group.Min(r => r.Minimum), max = group.Max(r => r.Maximum);
            // Zero origin for nonnegative metrics; a fixed negative origin for signed metrics.
            double origin = Math.Min(0, min);
            double step = Math.Max(group.Min(r => r.Resolution), (max - origin) / MaxSample);
            var encoding = new BleMetricEncoding(format, group.Key, string.IsNullOrEmpty(group.First().Unit) ? "raw value" : group.First().Unit, step, origin, min, max);
            var meanings = group.Select(r => r.Meaning).Distinct().ToArray();
            if (meanings.Length != 1) throw new InvalidOperationException($"Ambiguous BLE meaning for {format}:{group.Key}");
            entries.Add(encoding.Type, encoding with { Meaning = meanings[0] });
        }
        return entries;
    }
}
