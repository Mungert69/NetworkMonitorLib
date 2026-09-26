namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>Exact signed bytes for constrained processors; the authenticated payload
/// contains operation, target and JSON. No unsigned duplicate command is carried.</summary>
public sealed class ProcessorSignedCommand
{
    public int Version { get; set; } = 1;
    public string Algorithm { get; set; } = "ES256";
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}
