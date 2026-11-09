using NetworkMonitor.DTOs;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Utils.Helpers;

/// <summary>Optional context for interactive tools only. Reports retain their own projection.</summary>
public static class LlmMeasurementProjection
{
    public static LlmMeasurementBatch Project(IEnumerable<MonitorPingInfo> source, bool detail)
    {
        var contexts = new Dictionary<string, LlmMeasurementContext>();
        var ids = new Dictionary<EndpointMeasurementMetadata, string>();
        var rows = new List<LlmMeasurementRow>();
        foreach (var row in source)
        {
            var fields = LlmMeasurementRowProjector.Project(row, detail);
            if (detail)
            {
                // The catalogue reader supplies authoritative agent metadata, including custom Connects.
                // Without it, use conservative semantics rather than guessing a custom measurement.
                var definition = row.MeasurementContext ?? new EndpointMeasurementMetadata(
                    Unit: row.Unit, Scale: row.Scale, Offset: row.Offset,
                    Description: "Measurement definition unavailable.", AnalysisKind: "unspecified",
                    AnalysisGuidance: "Use supplied units and status only; do not infer device semantics or operating limits.");
                if (!ids.TryGetValue(definition, out var id))
                {
                    id = "m" + (ids.Count + 1);
                    ids.Add(definition, id);
                    contexts.Add(id, new LlmMeasurementContext(definition.Type, definition.Unit,
                        definition.Description, definition.AnalysisKind, definition.AnalysisGuidance,
                        definition.TimingRatingThresholds));
                }
                fields.MeasurementContextId = id;
            }
            rows.Add(fields);
        }
        return new LlmMeasurementBatch(rows, detail ? contexts : null);
    }
}
