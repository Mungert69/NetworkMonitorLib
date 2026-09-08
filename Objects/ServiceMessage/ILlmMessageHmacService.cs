namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>HMAC trust domain shared only by LLM instances and their trusted peers.</summary>
public interface ILlmMessageHmacService : IBackendMessageHmacService
{
}
