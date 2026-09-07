using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.ServiceMessage;

public interface IBackendMessageSignatureService
{
    Task<string> SignAsync(string operation, string target, IBackendSignedMessage message, CancellationToken cancellationToken = default);
}
