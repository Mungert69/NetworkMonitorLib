using System.Globalization;
using System.Text.RegularExpressions;
namespace NetworkMonitor.Connection;

/// <summary>Numeric advertised reading; null denotes unavailable or clipped data.</summary>
public sealed record BleReading(string Metric, double? Value, string Unit, double Resolution = 1)
{
    public static string MetricName(string label) => Regex.Replace(label.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
}
public sealed record BleDecodedPayload(string Message, IReadOnlyList<BleReading> Readings)
{
    public string MetricSummary => "Available numeric metrics: " + string.Join(", ", Readings.Select(r => r.Metric));
}

/// <summary>Shared metric aliases and ushort encoding. No saturation or elapsed-time fallback.</summary>
public static class BleMetricSelector
{
    public static string Canonical(string metric) => metric.ToLowerInvariant() switch
    {
        "pv" or "pvpower" => "pv_power",
        "battery_v" or "battery_voltage_v" => "battery_voltage",
        "battery_a" or "battery_current_a" => "battery_current",
        "load_a" or "load_current_a" => "load_current",
        "yield" or "yield_today_kwh" => "yield_today",
        _ => BleReading.MetricName(metric)
    };
    public static double DefaultScale(BleReading reading) => Regex.Replace(reading.Metric, @"_\d+$", "") switch
    {
        "battery_voltage" => 100,
        "battery_current" or "load_current" => 10,
        "yield_today" => 100,
        "temperature" or "dewpoint" or "battery_temperature" or "humidity" or "moisture" => 100,
        "pressure" => reading.Unit == "Pa" ? 0.1 : 10,
        _ => 1 / reading.Resolution
    };
    public static bool TrySelect(IReadOnlyList<BleReading> readings, string metric, double? scale,
        double offset, out ushort sample, out string label, out string error)
    {
        sample = 0; label = Canonical(metric); error = "";
        string name = label;
        var matches = readings.Where(r => r.Metric == name).ToArray();
        if (matches.Length != 1 || !matches[0].Value.HasValue)
        {
            error = $"BLE metric '{label}' is {(matches.Length > 1 ? "ambiguous" : matches.Length == 0 ? "missing or nonnumeric" : "unavailable")}. Available numeric metrics: {string.Join(", ", readings.Select(r => r.Metric).Distinct())}.";
            return false;
        }
        var reading = matches[0];
        double factor = scale ?? DefaultScale(reading);
        decimal encoded = -1;
        if (double.IsFinite(factor) && factor > 0 && double.IsFinite(offset) && double.IsFinite(reading.Value!.Value))
        {
            try { encoded = Math.Round(((decimal)reading.Value.Value + (decimal)offset) * (decimal)factor, 0, MidpointRounding.AwayFromZero); }
            catch (OverflowException) { }
        }
        if (!double.IsFinite(factor) || factor <= 0 || !double.IsFinite(offset) || encoded < 0 || encoded > BleMetricCatalogue.MaxSample)
        {
            error = $"BLE metric '{label}' does not fit 0..65534 with scale {factor.ToString(CultureInfo.InvariantCulture)} and offset {offset.ToString(CultureInfo.InvariantCulture)}. Set --metric_scale and/or --metric_offset.";
            return false;
        }
        sample = (ushort)encoded; return true;
    }
}
