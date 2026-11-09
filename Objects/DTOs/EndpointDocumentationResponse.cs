using System.Text.Json.Serialization;

namespace NetworkMonitor.DTOs;

public sealed record EndpointDocumentationResponse
{
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = "";
    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; init; }
    [JsonPropertyName("record")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Record { get; init; }
    [JsonPropertyName("metric")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Metric { get; init; }
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Key { get; init; }
    [JsonPropertyName("unit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; init; }
    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Payload { get; init; }
    [JsonPropertyName("analysis_kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AnalysisKind { get; init; }
    [JsonPropertyName("analysis_guidance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AnalysisGuidance { get; init; }
    [JsonPropertyName("availability")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Availability { get; init; }
    [JsonPropertyName("next")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Next { get; init; }
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, EndpointArgumentDocumentation>? Arguments { get; init; }
    [JsonPropertyName("advanced_arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, EndpointArgumentDocumentation>? AdvancedArguments { get; init; }
    [JsonPropertyName("formats")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Formats { get; init; }
    [JsonPropertyName("records")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BleRecordDocumentation>? Records { get; init; }
    [JsonPropertyName("metrics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BleMetricDocumentation>? Metrics { get; init; }
    [JsonPropertyName("example")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EndpointConfigurationExample? Example { get; init; }
}

public sealed record EndpointArgumentDocumentation(
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("default")] string Default,
    [property: JsonPropertyName("description")] string Description);

public sealed record BleRecordDocumentation(
    [property: JsonPropertyName("record")] string Record,
    [property: JsonPropertyName("record_type")] string RecordType);

public sealed record BleMetricDocumentation(
    [property: JsonPropertyName("metric")] string Metric,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("analysis_kind")] string AnalysisKind);

public sealed record EndpointConfigurationExample(
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("args")] string Args,
    [property: JsonPropertyName("address"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Address);

public sealed record EndpointDiscoveryResponse(
    [property: JsonPropertyName("agents")] IReadOnlyDictionary<string, List<string>> Agents,
    [property: JsonPropertyName("documentation")] EndpointDocumentationResponse Documentation);
