using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Utils;

/// <summary>
/// ML-DSA-65 signer shared by backend publishers. The private PEM remains a
/// read-only deployment secret; no key material is stored in messages.
/// </summary>
public sealed class BackendMessageSignatureService : IBackendMessageSignatureService
{
    private readonly string _privateKeyPath;
    private readonly string _openSslPath;
    private readonly ILogger _logger;

    public BackendMessageSignatureService(IConfiguration configuration, ILogger logger)
    {
        _privateKeyPath = configuration["AuthKeySigning:PrivateKeyPath"]
            ?? Path.Combine("keys", "authkey-signing-ml-dsa-65-private.pem");
        _openSslPath = configuration["AuthKeySigning:OpenSslPath"] ?? "openssl";
        _logger = logger;
    }

    public Task<string> SignAsync(string operation, string target, IBackendSignedMessage message, CancellationToken cancellationToken = default)
    {
        return SignPayloadAsync(BackendMessageSignaturePayload.Create(operation, target, message), operation, cancellationToken);
    }

    internal async Task<string> SignPayloadAsync(byte[] payload, string description, CancellationToken cancellationToken)
    {
        if (!File.Exists(_privateKeyPath))
        {
            throw new InvalidOperationException("AuthKey signing private key cannot be read.");
        }

        // ML-DSA is a one-shot algorithm. OpenSSL must know the input size, so
        // its pkeyutl implementation cannot sign a payload supplied through stdin.
        var payloadPath = Path.Combine(Path.GetTempPath(), $"networkmonitor-backend-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(payloadPath, payload, cancellationToken).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo
            {
                FileName = _openSslPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("pkeyutl");
            startInfo.ArgumentList.Add("-sign");
            startInfo.ArgumentList.Add("-rawin");
            startInfo.ArgumentList.Add("-in");
            startInfo.ArgumentList.Add(payloadPath);
            startInfo.ArgumentList.Add("-inkey");
            startInfo.ArgumentList.Add(_privateKeyPath);
            startInfo.ArgumentList.Add("-provider");
            startInfo.ArgumentList.Add("default");

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException("Unable to start OpenSSL for backend signing.");

            await using var signature = new MemoryStream();
            var copySignature = process.StandardOutput.BaseStream.CopyToAsync(signature, cancellationToken);
            var readError = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(copySignature, readError, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                _logger.LogError("OpenSSL failed to sign {Description}: {Error}", description, (await readError).Trim());
                throw new InvalidOperationException($"OpenSSL failed to sign {description}.");
            }

            return Convert.ToBase64String(signature.ToArray());
        }
        finally
        {
            try { File.Delete(payloadPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
