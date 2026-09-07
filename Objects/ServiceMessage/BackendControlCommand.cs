namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>
/// Signed envelope for a backend control operation that otherwise has no
/// payload.  The operation is included in the ML-DSA signed bytes, so this
/// envelope cannot be moved to a different RabbitMQ control exchange.
/// </summary>
public sealed class BackendControlCommand : IBackendSignedMessage
{
    public string BackendSignature { get; set; } = string.Empty;
}
