namespace NetworkMonitor.Objects.ServiceMessage;

public sealed class FunctionRegistryReply : IBackendSignedMessage
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public string? CatalogJson { get; init; }
    public string BackendSignature { get; set; } = string.Empty;
}
