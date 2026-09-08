using Microsoft.Extensions.Configuration;

namespace NetworkMonitor.Objects.ServiceMessage;

public sealed class LlmMessageHmacService : MessageHmacServiceBase, ILlmMessageHmacService
{
    public LlmMessageHmacService(IConfiguration configuration)
        : base(configuration, "LLMMessageHmacKey")
    {
    }
}
