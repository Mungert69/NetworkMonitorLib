namespace NetworkMonitor.Connection;

/// <summary>Optional duration-rating boundaries in milliseconds, not alert thresholds.</summary>
public sealed record TimingRatingThresholds(double Excellent, double Good, double Fair)
{
    public bool IsValid() => double.IsFinite(Excellent) && double.IsFinite(Good) && double.IsFinite(Fair)
        && Excellent > 0 && Excellent < Good && Good < Fair;
}

public static class TimingMeasurementRating
{
    public static bool IsEnabled(EndpointMeasurementMetadata definition) => definition.AnalysisKind == "duration"
        && definition.Unit == "ms" && definition.TimingRatingThresholds is { } thresholds && thresholds.IsValid();

    public static string? Category(double? value, EndpointMeasurementMetadata definition)
    {
        if (!IsEnabled(definition)) return null;
        if (!value.HasValue || !double.IsFinite(value.Value)) return "bad";
        var thresholds = definition.TimingRatingThresholds!;
        return value.Value < thresholds.Excellent ? "excellent" : value.Value < thresholds.Good ? "good"
            : value.Value < thresholds.Fair ? "fair" : "poor";
    }
}
