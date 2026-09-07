using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Utils;

public static class BackendMessageSigner
{
    private static readonly string[] ProtectedOperations =
    {
        "getCmdProcessorSource", "getCmdProcessorHelp", "getCmdProcessorList",
        "deleteCmdProcessor", "addCmdProcessor", "processorCommand",
        "processorQueueDic", "processorScan", "cancelCommand",
        "getConnectSource", "getConnectList", "deleteConnect", "addConnect",
        "processorInit"
    };

    public static bool TryGetProtectedOperation(string exchangeName, out string operation, out string appId)
    {
        foreach (var candidate in ProtectedOperations)
        {
            if (exchangeName.StartsWith(candidate, StringComparison.Ordinal) && exchangeName.Length > candidate.Length)
            {
                operation = candidate;
                appId = exchangeName[candidate.Length..];
                return true;
            }
        }
        operation = string.Empty;
        appId = string.Empty;
        return false;
    }

    public static async Task SignIfRequiredAsync(string exchangeName, object? value)
    {
        if (value is not IBackendSignedMessage signedMessage ||
            !TryGetProtectedOperation(exchangeName, out var operation, out var appId))
        {
            return;
        }

        var privateKeyPath = Environment.GetEnvironmentVariable("AuthKeySigning__PrivateKeyPath")
            ?? Path.Combine("keys", "authkey-signing-ml-dsa-65-private.pem");
        var openSslPath = Environment.GetEnvironmentVariable("AuthKeySigning__OpenSslPath") ?? "openssl";
        if (!File.Exists(privateKeyPath))
        {
            throw new InvalidOperationException("Backend signing private key cannot be read.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = openSslPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("pkeyutl");
        startInfo.ArgumentList.Add("-sign");
        startInfo.ArgumentList.Add("-rawin");
        startInfo.ArgumentList.Add("-inkey");
        startInfo.ArgumentList.Add(privateKeyPath);
        startInfo.ArgumentList.Add("-provider");
        startInfo.ArgumentList.Add("default");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Unable to start OpenSSL for backend message signing.");

        var payload = BackendMessageSignaturePayload.Create(operation, appId, signedMessage);
        await process.StandardInput.BaseStream.WriteAsync(payload).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();

        await using var signature = new MemoryStream();
        var copySignature = process.StandardOutput.BaseStream.CopyToAsync(signature);
        var readError = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(copySignature, readError, process.WaitForExitAsync()).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"OpenSSL failed to sign backend message: {(await readError).Trim()}");
        }
        signedMessage.BackendSignature = Convert.ToBase64String(signature.ToArray());
    }
}
