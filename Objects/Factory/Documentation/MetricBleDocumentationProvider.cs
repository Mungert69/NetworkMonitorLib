using NetworkMonitor.Connection;
using NetworkMonitor.DTOs;

namespace NetworkMonitor.Objects.Factory;

/// <summary>Shared metric discovery and examples for protocols backed by the measurement catalogue.</summary>
public class MetricBleDocumentationProvider(string format, string key, string exampleMetric, string availability) : IBleFormatDocumentationProvider
{
    public string Format => format;
    protected string Key => key;
    public virtual EndpointDocumentationResponse Describe(EndpointDocumentationQuery query)
    {
        if (query.Record != null) throw new ArgumentException("This format does not support a record filter.");
        return DescribeMetrics(query);
    }

    protected EndpointDocumentationResponse DescribeMetrics(EndpointDocumentationQuery query, IReadOnlyCollection<string>? allowed = null)
    {
        if (query.Metric != null)
        {
            var metric = BleMetricSelector.Canonical(query.Metric);
            var definition = BleMetricCatalogue.Find(Format, metric);
            if (definition == null || (allowed != null && !allowed.Contains(definition.Metric)))
                throw new ArgumentException("Metric is not supported by the selected format/record. Request its metric list.");
            return new() {
                Endpoint = query.Endpoint, Format = Format, Record = query.Record, Metric = metric, Key = Key,
                Unit = definition.Unit, Description = definition.Meaning.Description,
                AnalysisKind = definition.Meaning.AnalysisKind, AnalysisGuidance = definition.Meaning.AnalysisGuidance,
                AdvancedArguments = BleArgumentDocumentation.Select(false, "raw_payload", "metric_scale", "metric_offset"),
                Availability = "A metric must be present and valid in the device's advertisement. Custom encoding overrides fall back to raw display metadata. Code meanings and operating limits are not inferred.",
                Example = BleArgumentDocumentation.Example(Format, metric)
            };
        }
        var definitions = BleMetricCatalogue.Definitions.Where(d => d.Format == Format && (allowed == null || allowed.Contains(d.Metric))).OrderBy(d => d.Metric).ToArray();
        var example = definitions.FirstOrDefault(d => d.Metric == exampleMetric) ?? definitions.First();
        return new() {
            Endpoint = query.Endpoint, Format = Format, Record = query.Record, Key = Key,
            Payload = BlePayloadDecoderRegistry.Default.Find(Format)?.DefaultPayloadMode,
            Metrics = definitions.Select(d => new BleMetricDocumentation(d.Metric, d.Unit, d.Meaning.Description, d.Meaning.AnalysisKind)).ToArray(),
            Availability = availability, Example = BleArgumentDocumentation.Example(Format, example.Metric),
            Next = "Specify metric for detailed interpretation guidance. Record is not an Args flag."
        };
    }
}

public sealed class VictronDocumentationProvider() : MetricBleDocumentationProvider("victron", "Required: 16-byte Victron advertising key in password.", "battery_voltage",
    "Available metrics depend on the advertised record and optional fields; solar load_current requires the extended payload.")
{
    public override EndpointDocumentationResponse Describe(EndpointDocumentationQuery query)
    {
        var records = VictronDeviceRecordDecoders.CreateDefaults().ToArray();
        if (query.Record == null && query.Metric == null)
            return new() {
                Endpoint = query.Endpoint, Format = Format, Key = Key,
                Records = records.Select(r => new BleRecordDocumentation(r.RecordName, $"0x{r.RecordType:x2}")).ToArray(),
                Next = "Specify record to list its metrics. Record is a discovery filter, not a host Args flag; the packet selects its record automatically."
            };
        var record = query.Record == null ? null : records.FirstOrDefault(r => r.RecordName == query.Record)
            ?? throw new ArgumentException("Unknown record; request format=victron without record to list valid records.");
        return DescribeMetrics(query, record?.Metrics.Select(m => m.Metric).ToArray());
    }
}
