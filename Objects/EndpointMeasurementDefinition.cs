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

    public bool IsValid() => !string.IsNullOrWhiteSpace(EndpointType)
        && EndpointType.Length <= 50 && EndpointType == EndpointType.Trim()
        && !EndpointType.Any(char.IsControl)
        && !string.IsNullOrWhiteSpace(Unit) && Unit.Length <= 32
        && Unit == Unit.Trim() && !Unit.Any(char.IsControl)
        && Type != null && Type.Length <= 50 && Type == Type.Trim() && !Type.Any(char.IsControl)
        && double.IsFinite(Scale) && Scale > 0;
}
