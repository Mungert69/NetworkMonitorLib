namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>Owner-authorized deletion of a particular processor registration.</summary>
public sealed class ProcessorDeleteRequest : IBackendSignedMessage
{
    public string AppID { get; set; } = string.Empty;
    public int RegistrationId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public System.DateTime ExpiresAtUtc { get; set; }
    public string BackendSignature { get; set; } = string.Empty;
}
