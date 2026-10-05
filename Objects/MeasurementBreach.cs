using NetworkMonitor.Connection;
using NetworkMonitor.Utils.Helpers;
namespace NetworkMonitor.Objects;

/// <summary>Latched physical limit violation; independent of probe availability.</summary>
public sealed record MeasurementBreach(string Direction, double Value, double Limit, string Unit, DateTime ObservedAt);

public static class MeasurementAlertLimits
{
    public static bool IsValid(double? low, double? high) =>
        (!low.HasValue || double.IsFinite(low.Value)) && (!high.HasValue || double.IsFinite(high.Value))
        && (!low.HasValue || !high.HasValue || low.Value < high.Value);

    public static MeasurementBreach? Evaluate(ushort sample, EndpointMeasurementMetadata metadata,
        double? low, double? high, DateTime observedAt)
    {
        if (!IsValid(low, high) || sample == ushort.MaxValue) return null;
        var value = MeasurementConversion.Value(sample, metadata.Scale, metadata.Offset);
        if (!value.HasValue) return null;
        // Account only for arithmetic roundoff from scale/offset cancellation.
        var tolerance = 8 * 2.220446049250313e-16 * (Math.Abs(sample * metadata.Scale) + Math.Abs(metadata.Offset) + Math.Abs(value.Value));
        return low.HasValue && value.Value < low.Value - tolerance ? new("low", value.Value, low.Value, metadata.Unit, observedAt)
            : high.HasValue && value.Value > high.Value + tolerance ? new("high", value.Value, high.Value, metadata.Unit, observedAt) : null;
    }
}
