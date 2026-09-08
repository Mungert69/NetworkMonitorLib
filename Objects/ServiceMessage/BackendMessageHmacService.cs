using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>Fast payload authentication for higher-volume backend messages.</summary>
public abstract class MessageHmacServiceBase : IBackendMessageHmacService
{
    private readonly byte[] _key;

    protected MessageHmacServiceBase(IConfiguration configuration, string configurationKey)
    {
        var configuredKey = configuration[configurationKey];
        if (string.IsNullOrWhiteSpace(configuredKey))
            throw new InvalidOperationException($"{configurationKey} is required.");
        _key = Encoding.UTF8.GetBytes(configuredKey);
        if (_key.Length < 32)
            throw new InvalidOperationException($"{configurationKey} must contain at least 32 UTF-8 bytes.");
    }

    public Task<string> SignAsync(string operation, string target, IBackendSignedMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mac = HMACSHA256.HashData(_key, BackendMessageSignaturePayload.Create(operation, target, message));
        return Task.FromResult(Convert.ToBase64String(mac));
    }

    public async Task<bool> VerifyAsync(string operation, string target, IBackendSignedMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.BackendSignature)) return false;
        byte[] supplied;
        try { supplied = Convert.FromBase64String(message.BackendSignature); }
        catch (FormatException) { return false; }
        var expected = Convert.FromBase64String(await SignAsync(operation, target, message, cancellationToken).ConfigureAwait(false));
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
}

public sealed class BackendMessageHmacService : MessageHmacServiceBase
{
    public BackendMessageHmacService(IConfiguration configuration)
        : base(configuration, "BackendMessageHmacKey")
    {
    }
}
