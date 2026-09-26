namespace NetworkMonitor.Objects.ServiceMessage;

public interface IProcessorCommandSigner
{
    bool RequiresEcdsa(string target);
    ProcessorSignedCommand Sign(string operation, string target, object message);
}
