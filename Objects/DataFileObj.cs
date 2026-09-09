using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Objects;

public class DataFileObj : IBackendSignedMessage
{
    public string BackendSignature { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Url { get; set; } = "";
    public byte[]? Data { get; set; }
    public string? Json { get; set; }

    public string? Html { get; set; }

}
