namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>A backend-signed request to update one registered processor.</summary>
public sealed class ProcessorFirmwareUpdateRequest : IBackendSignedMessage
{
    public string AppID { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public System.DateTime ExpiresAtUtc { get; set; }
    public string BackendSignature { get; set; } = string.Empty;
}
