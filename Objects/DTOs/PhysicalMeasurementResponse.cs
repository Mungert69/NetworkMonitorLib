using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Connection;

namespace NetworkMonitor.DTOs;

/// <summary>Physical values for downloads and analysis; no second scaling is required.</summary>
public sealed record PhysicalMeasurementReading(DateTime Timestamp, double? Value, string? Status);
public sealed record PhysicalMeasurementResponse(string? Address, string? Endpoint, string? Metric,
    string Unit, double? Average, double? Minimum, double? Maximum, double? Total,
    double? StandardDeviation, IReadOnlyList<PhysicalMeasurementReading> Readings)
{
    public static PhysicalMeasurementResponse From(HostResponseObj source)
    {
        var args = string.IsNullOrWhiteSpace(source.Args) ? source.Username : source.Args;
        double? Value(double sample) => source.PacketsRecieved > 0
            ? MeasurementConversion.Value(sample, source.Scale, source.Offset) : null;
        return new(source.Address, source.EndPointType,
            source.EndPointType == "blebroadcast" ? BleMetricCatalogue.MetricFromArgs(args) : null,
            source.Unit, Value(source.RoundTripTimeAverage), Value(source.RoundTripTimeMinimum),
            Value(source.RoundTripTimeMaximum), MeasurementConversion.Total(source.RoundTripTimeTotal,
                source.PacketsRecieved, source.Scale, source.Offset),
            source.PacketsRecieved > 0 ? MeasurementConversion.StandardDeviation(source.RoundTripTimeStandardDeviation, source.Scale) : null,
            source.PingInfosDTO.Select(p => new PhysicalMeasurementReading(p.DateSent,
                MeasurementConversion.Value(p.ResponseTime, source.Scale, source.Offset), p.Status)).ToArray());
    }
}
