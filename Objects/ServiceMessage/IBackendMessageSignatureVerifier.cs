using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>
/// Verifies a trusted backend ML-DSA signature. Verifiers use only the public
/// key; publishing services are the only components that need the private key.
/// </summary>
public interface IBackendMessageSignatureVerifier
{
    Task<bool> VerifyAsync(string operation, string target, IBackendSignedMessage message, CancellationToken cancellationToken = default);
}
