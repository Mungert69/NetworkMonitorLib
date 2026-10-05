using System;

namespace NetworkMonitor.Utils.Helpers;

/// <summary>Converts stored samples for physical-value consumers; API samples remain encoded.</summary>
public static class MeasurementConversion
{
    public static double? Value(double sample, double scale, double offset)
    {
        if (!double.IsFinite(sample) || sample < 0 || sample == ushort.MaxValue ||
            !double.IsFinite(scale) || scale <= 0 || !double.IsFinite(offset)) return null;
        return Finite(sample * scale + offset);
    }

    public static double? StandardDeviation(double deviation, double scale) =>
        double.IsFinite(deviation) && deviation >= 0 && double.IsFinite(scale) && scale > 0
            ? Finite(deviation * scale) : null;

    public static double? Total(double total, int validCount, double scale, double offset) =>
        validCount > 0 && double.IsFinite(total) && total >= 0 && double.IsFinite(scale) && scale > 0 && double.IsFinite(offset)
            ? Finite(total * scale + validCount * offset) : null;

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}
