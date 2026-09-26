namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>
/// C-processor readiness on the existing processor/out/ready topic.
/// Authenticated with the processor AuthKey and broker permissions, not the
/// IBackendSignedMessage/ML-DSA contract. A future device signature scheme belongs
/// to this contract rather than ProcessorInitObj.
/// </summary>
public sealed class CProcessorReadyObj
{
    public string AppID { get; set; } = string.Empty;
    public string AuthKey { get; set; } = string.Empty;
    public bool IsProcessorReady { get; set; }
    public int RabbitTopologyVersion { get; set; }
    public bool IsQuantumCapable { get; set; } = true;
}
