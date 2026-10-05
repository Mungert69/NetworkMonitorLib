namespace NetworkMonitor.Connection;

/// <summary>Reusable wording for explicitly declared measurement kinds; no endpoint or decoder lookup.</summary>
public static class MeasurementAnalysisTemplates
{
    public static EndpointMeasurementMetadata Complete(EndpointMeasurementMetadata metadata)
    {
        string label = string.IsNullOrEmpty(metadata.Type) ? "Unspecified measurement" : metadata.Type;
        var fallback = metadata.AnalysisKind == "duration" ? Duration(label, metadata.TimingRatingThresholds) : Metric(label, metadata.Unit,
            string.IsNullOrWhiteSpace(metadata.AnalysisKind) ? "unspecified" : metadata.AnalysisKind);
        return metadata with {
            Description = string.IsNullOrWhiteSpace(metadata.Description) ? fallback.Description : metadata.Description,
            AnalysisKind = string.IsNullOrWhiteSpace(metadata.AnalysisKind) ? fallback.AnalysisKind : metadata.AnalysisKind,
            AnalysisGuidance = string.IsNullOrWhiteSpace(metadata.AnalysisGuidance) ? fallback.AnalysisGuidance : metadata.AnalysisGuidance
        };
    }

    public static EndpointMeasurementMetadata Duration(string operation, TimingRatingThresholds? thresholds = null) => new(
        Description: $"Elapsed time for {operation} in milliseconds.", AnalysisKind: "duration",
        TimingRatingThresholds: thresholds,
        AnalysisGuidance: thresholds == null
            ? "Describe operation completion time, variation and availability. Use statuses to explain outcomes. This duration has no configured timing ratings; do not label it excellent, good, fair or poor, assume faster means a better result, or apply network latency limits."
            : """
        Identify noticeable spikes or periods of high response times, including approximate timing.
        Detect any timeouts, noting their frequency and period of occurrence.
        Assess the overall consistency of response times, highlighting stability or fluctuations.
        The Overall Average Response Time metric in your summary should reflect the total reporting period, not the individual sample averages.
        Use supplied response-time categories when present: excellent means very fast response times; good means acceptable response times with minor delays; fair means moderate delays; poor means significant delays; bad indicates a timeout or unresponsive period.
        Performance Assessment: Provide a concise summary of critical performance metrics, highlighting any spikes, patterns, timeouts, or stability over the reporting period.
        Expert Recommendations: Offer actionable recommendations based on the observed data trends, such as measures to address high response times or timeouts, or suggestions for optimizing server performance.
        This report is generated from a network monitoring system that is already in place. Recommendations should focus on actionable measures to improve performance, optimize resource allocation, and address detected issues, rather than suggesting the implementation of monitoring itself.
        Focus on summarizing key trends without listing individual data points. Each field in the JSON output should provide essential insights to ensure an informative, concise summary.
        """);

    public static EndpointMeasurementMetadata Metric(string label, string unit, string kind = "continuous")
    {
        unit = string.IsNullOrEmpty(unit) ? "raw value" : unit;
        string description = $"{label} in {unit}.", guidance =
            "Describe trends, variability and missing readings. Do not invent operating limits or infer health without device context.";
        switch (kind) {
            case "state":
                description = $"Binary {label} state: 0 inactive, 1 active.";
                guidance = "Describe observed state transitions and availability. Do not treat averages as physical measurements or infer time in a state without adequate timestamp coverage."; break;
            case "event":
                description = $"{label} event code or event step count.";
                guidance = "Describe observed events. Do not average event codes, invent code meanings, or count repeated broadcasts as distinct events without deduplication."; break;
            case "sequence":
                description = $"Protocol {label} identifier or sequence value.";
                guidance = "Describe changes, gaps and possible wraparound. These identifiers are not physical measurements or device health scores; averages are not meaningful."; break;
            case "timestamp":
                description = "Device-reported Unix timestamp in seconds.";
                guidance = "Compare device timestamps with observation times cautiously. Do not infer latency or synchronized clocks without clock context."; break;
            case "counter":
                description = $"Accumulated {label} in {unit}; reset and rollover semantics depend on the device.";
                guidance = "Analyse observed changes and possible resets or rollover. Do not sum cumulative samples, interpret counter drops as negative usage, or infer rates without timestamp coverage. Daily yield may reset daily."; break;
            case "code":
                description = $"Protocol {label} numeric code or flags.";
                guidance = "Describe changes and supplied status meanings. Do not average codes or invent enum/bit meanings."; break;
            case "unspecified":
                guidance = "Describe availability and supplied context only. Measurement semantics are unspecified; do not infer physical meaning or operating limits."; break;
            case "continuous":
                if (unit == "V") {
                    description = $"{label}: electrical potential difference in volts.";
                    guidance += " Do not assume battery chemistry, nominal voltage or safe charging limits. Cell, auxiliary and terminal voltages are different measurements.";
                }
                else if (unit == "A") guidance += " Negative current may indicate flow direction; do not assume the device's sign convention.";
                else if (unit == "°C") {
                    description = $"{label} in degrees Celsius.";
                    guidance += " Negative temperatures are valid. Ambient, battery and dew-point temperatures require different context.";
                }
                else if (unit is "W" or "VA") guidance += " Distinguish power from accumulated energy; do not assume load capacity or flow direction.";
                break;
        }
        return new(Unit: unit, Description: description, AnalysisKind: kind, AnalysisGuidance: guidance);
    }
}
