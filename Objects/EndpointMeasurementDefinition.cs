using System;
using System.Linq;

namespace NetworkMonitor.Objects;

/// <summary>Display metadata owned by a Connect, independent of individual samples.</summary>
public class EndpointMeasurementDefinition
{
    public string EndpointType { get; set; } = "";
    // Subtype token matched in host Args; blank is the general/fallback definition.
    public string Type { get; set; } = "";
    public string Unit { get; set; } = "ms";
    public double Scale { get; set; } = 1;
    public double Offset { get; set; } = 0;
    public string Description { get; set; } = "";
    public string AnalysisKind { get; set; } = "";
    public string AnalysisGuidance { get; set; } = "";


    public NetworkMonitor.Connection.TimingRatingThresholds? TimingRatingThresholds { get; set; }

    public bool IsValid() => !string.IsNullOrWhiteSpace(EndpointType)
        && EndpointType.Length <= 50 && EndpointType == EndpointType.Trim()
        && !EndpointType.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(Unit) && Unit.Length <= 32
        && Unit == Unit.Trim() && !Unit.Any(char.IsControl)
        && Type != null && Type.Length <= 50 && Type == Type.Trim() && !Type.Any(char.IsControl)
        && Description != null && Description.Length <= 512
        && AnalysisKind != null && AnalysisKind.Length <= 32
        && AnalysisGuidance != null && AnalysisGuidance.Length <= 2048
        && (TimingRatingThresholds == null || (AnalysisKind == "duration" && Unit == "ms" && TimingRatingThresholds.IsValid()))
        && double.IsFinite(Scale) && Scale > 0 && double.IsFinite(Offset);
}
