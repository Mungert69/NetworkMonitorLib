using NetworkMonitor.Connection;
using NetworkMonitor.DTOs;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Utils.Helpers;

/// <summary>Builds interactive measurement response objects without serializing or mutating stored data.</summary>
public static class LlmMeasurementRowProjector
{
    public static LlmMeasurementDateRange DateRange(MonitorPingInfo source, TimeZoneInfo clientTimeZone) =>
        new(StringUtils.FormatDateTime(source.DateStarted, clientTimeZone),
            source.DateEnded.HasValue ? StringUtils.FormatDateTime(source.DateEnded.Value, clientTimeZone) : null);

    public static LlmMeasurementRow Project(MonitorPingInfo source, bool detail)
    {
        // Shared object projection; serialization happens only at the response boundary.
        double? Convert(double sample) => source.PacketsRecieved > 0
            ? MeasurementConversion.Value(sample, source.Scale, source.Offset) : null;
        LlmMeasurementRow row = detail
            ? new LlmDetailedMeasurementRow {
                DatasetId = source.DataSetID, Status = source.MonitorStatus?.Message,
                PacketsSent = source.PacketsSent, PacketsReceived = source.PacketsRecieved,
                PacketsLost = source.PacketsLost, PacketsLostPercentage = source.PacketsLostPercentage,
                AlertSent = source.MonitorStatus?.AlertSent, AlertFlag = source.MonitorStatus?.AlertFlag,
                RoundTripTimeMaximum = Convert(source.RoundTripTimeMaximum),
                RoundTripTimeAverage = Convert(source.RoundTripTimeAverage)
            } : new LlmMeasurementRow();
        row.StatusMessage = source.MessageForUser;
        row.Id = source.ID;
        row.Address = source.Address;
        row.Endpoint = source.EndPointType;
        row.AgentLocation = source.AgentLocation;
        row.Unit = source.Unit;
        row.LowThreshold = source.LowThreshold;
        row.HighThreshold = source.HighThreshold;
        row.MeasurementAverage = Convert(source.RoundTripTimeAverage);
        row.MeasurementMinimum = Convert(source.RoundTripTimeMinimum);
        row.MeasurementMaximum = Convert(source.RoundTripTimeMaximum);
        row.MeasurementBreach = source.MeasurementBreach;
        var args = string.IsNullOrWhiteSpace(source.Args) ? source.Username : source.Args;
        if (source.EndPointType == "blebroadcast") row.Metric = BleMetricCatalogue.MetricFromArgs(args);
        if (source.Port != 0) row.Port = source.Port;
        return row;
    }
}
