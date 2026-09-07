using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>
/// OpenSSL ML-DSA verifier for backend messages. It deliberately accepts a
/// public key only: receiving services must never be given the signing key.
/// </summary>
public sealed class BackendMessageSignatureVerifier : IBackendMessageSignatureVerifier
{
    private readonly string _openSslPath;
    private readonly ILogger<BackendMessageSignatureVerifier> _logger;

    public BackendMessageSignatureVerifier(IConfiguration configuration, ILogger<BackendMessageSignatureVerifier> logger)
    {
        _openSslPath = configuration["AuthKeySigning:OpenSslPath"] ?? "openssl";
        _logger = logger;
    }

    public async Task<bool> VerifyAsync(string operation, string target, IBackendSignedMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message.BackendSignature))
        {
            _logger.LogWarning("ML-DSA signature is unavailable for {Operation}.", operation);
            return false;
        }

        byte[] signature;
        try { signature = Convert.FromBase64String(message.BackendSignature); }
        catch (FormatException) { return false; }

        var prefix = Path.Combine(Path.GetTempPath(), $"networkmonitor-backend-{Guid.NewGuid():N}");
        var signaturePath = prefix + ".sig";
        var publicKeyPath = prefix + ".pub.pem";
        try
        {
            await File.WriteAllBytesAsync(signaturePath, signature, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(publicKeyPath, BackendSigningTrustAnchor.PublicKeyPem, cancellationToken).ConfigureAwait(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = _openSslPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("pkeyutl");
            startInfo.ArgumentList.Add("-verify");
            startInfo.ArgumentList.Add("-rawin");
            startInfo.ArgumentList.Add("-pubin");
            startInfo.ArgumentList.Add("-inkey");
            startInfo.ArgumentList.Add(publicKeyPath);
            startInfo.ArgumentList.Add("-sigfile");
            startInfo.ArgumentList.Add(signaturePath);
            startInfo.ArgumentList.Add("-provider");
            startInfo.ArgumentList.Add("default");

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return false;
            var payload = BackendMessageSignaturePayload.Create(operation, target, message);
            await process.StandardInput.BaseStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(output, error, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);
            if (process.ExitCode == 0) return true;

            _logger.LogWarning("ML-DSA verification failed for {Operation}: {Error}", operation, (await error).Trim());
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "ML-DSA verification could not be completed for {Operation}.", operation);
            return false;
        }
        finally
        {
            try { File.Delete(signaturePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try { File.Delete(publicKeyPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
