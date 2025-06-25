namespace NetworkMonitor.Objects.ServiceMessage;
public sealed class FunctionRegistryReply
{
    public required bool   Success      { get; init; }
    public required string Message      { get; init; }
    public string?         CatalogJson  { get; init; }
}