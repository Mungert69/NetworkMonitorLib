namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>Device-facing update notification; the firmware is independently signed.</summary>
public sealed class ProcessorFirmwareUpdateCommand
{
    public string AppID { get; set; } = string.Empty;
    public long ExpiresAtUnixSeconds { get; set; }
    public string AuthKey { get; set; } = string.Empty;
    public string UpdateUrl { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
}
