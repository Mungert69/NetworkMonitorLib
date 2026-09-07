namespace NetworkMonitor.Objects.ServiceMessage;

public interface IBackendSignedMessage
{
    string BackendSignature { get; set; }
}
