namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>Data's confirmation that it processed an updated processor's ready event.</summary>
public sealed class ProcessorFirmwareHealthAck
{
    public string AuthKey { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
