using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.ServiceMessage;

public interface IBackendMessageHmacService
{
    Task<string> SignAsync(string operation, string target, IBackendSignedMessage message, CancellationToken cancellationToken = default);
    Task<bool> VerifyAsync(string operation, string target, IBackendSignedMessage message, CancellationToken cancellationToken = default);
}
