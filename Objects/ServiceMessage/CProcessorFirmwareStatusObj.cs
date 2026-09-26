namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>
/// Device OTA status on processor/out/firmware-status. Uses the C-device AuthKey
/// trust model, independently of the backend ML-DSA signing contract.
/// </summary>
public sealed class CProcessorFirmwareStatusObj
{
    public string AppID { get; set; } = string.Empty;
    public string AuthKey { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // PendingConfirmation or Confirmed
}
