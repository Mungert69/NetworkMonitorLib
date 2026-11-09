using System.Text.Json.Serialization;
using NetworkMonitor.Objects;
using NetworkMonitor.Connection;

namespace NetworkMonitor.DTOs;

[JsonDerivedType(typeof(LlmDetailedMeasurementRow))]
public class LlmMeasurementRow
{
    [JsonPropertyName("status_message")]
    public string? StatusMessage { get; set; }
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("address")]
    public string? Address { get; set; }
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = "";
    [JsonPropertyName("agent_location")]
    public string? AgentLocation { get; set; }
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "";
    [JsonPropertyName("low_threshold")]
    public double? LowThreshold { get; set; }
    [JsonPropertyName("high_threshold")]
    public double? HighThreshold { get; set; }
    [JsonPropertyName("measurement_average")]
    public double? MeasurementAverage { get; set; }
    [JsonPropertyName("measurement_minimum")]
    public double? MeasurementMinimum { get; set; }
    [JsonPropertyName("measurement_maximum")]
    public double? MeasurementMaximum { get; set; }
    [JsonPropertyName("measurement_breach")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MeasurementBreach? MeasurementBreach { get; set; }
    [JsonPropertyName("metric")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Metric { get; set; }
    [JsonPropertyName("port")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ushort? Port { get; set; }
    [JsonPropertyName("measurement_context_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MeasurementContextId { get; set; }
}

public sealed class LlmDetailedMeasurementRow : LlmMeasurementRow
{
    [JsonPropertyName("dataset_id")]
    public int DatasetId { get; set; }
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    [JsonPropertyName("packets_sent")]
    public int PacketsSent { get; set; }
    [JsonPropertyName("packets_received")]
    public int PacketsReceived { get; set; }
    [JsonPropertyName("packets_lost")]
    public int PacketsLost { get; set; }
    [JsonPropertyName("packets_lost_percentage")]
    public float PacketsLostPercentage { get; set; }
    [JsonPropertyName("alert_sent")]
    public bool? AlertSent { get; set; }
    [JsonPropertyName("alert_flag")]
    public bool? AlertFlag { get; set; }
    [JsonPropertyName("round_trip_time_maximum")]
    public double? RoundTripTimeMaximum { get; set; }
    [JsonPropertyName("round_trip_time_average")]
    public double? RoundTripTimeAverage { get; set; }
}

public sealed record LlmMeasurementContext(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("analysis_kind")] string AnalysisKind,
    [property: JsonPropertyName("analysis_guidance")] string AnalysisGuidance,
    [property: JsonPropertyName("timing_rating_thresholds")] TimingRatingThresholds? TimingRatingThresholds);

public sealed record LlmMeasurementBatch(
    [property: JsonPropertyName("data")] IReadOnlyList<LlmMeasurementRow> Data,
    [property: JsonPropertyName("measurement_contexts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, LlmMeasurementContext>? MeasurementContexts);

public sealed record LlmMeasurementDataResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<LlmMeasurementRow> Data,
    [property: JsonPropertyName("data_date_range"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LlmMeasurementDateRange? DateRange,
    [property: JsonPropertyName("measurement_contexts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, LlmMeasurementContext>? MeasurementContexts);

public sealed record LlmMeasurementToolResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("dataJson")] IReadOnlyList<LlmMeasurementRow> Data,
    [property: JsonPropertyName("measurement_contexts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, LlmMeasurementContext>? MeasurementContexts);

public sealed record LlmMeasurementDateRange(
    [property: JsonPropertyName("date_started")] string DateStarted,
    [property: JsonPropertyName("date_ended"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DateEnded);
