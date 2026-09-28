using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Objects.Repository.Helpers;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Utils;

/// <summary>Selects ECDSA from shared processor state, independent of transport.
/// Missing records retain the default quantum-capable behavior.</summary>
public sealed class EcdsaProcessorCommandSigner : IProcessorCommandSigner
{
    private readonly IProcessorState _processors;
    private readonly string? _keyPath;

    public EcdsaProcessorCommandSigner(IConfiguration configuration, IProcessorState processors)
    {
        _processors = processors;
        _keyPath = configuration["ProcessorCommandSigning:PrivateKeyPath"];
    }

    public bool RequiresEcdsa(string target) => _processors.GetProcessorListAll(false).Any(p =>
        !p.IsQuantumCapable && string.Equals(ProcessorRabbitTopology.GetRoutingId(p.AppID), target, StringComparison.Ordinal));

    public ProcessorSignedCommand Sign(string operation, string target, object message)
    {
        if (!RequiresEcdsa(target)) throw new InvalidOperationException("Processor is not registered as non-quantum-capable.");
        if (string.IsNullOrWhiteSpace(_keyPath))
            throw new InvalidOperationException("Processor command signing key path is missing.");
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(_keyPath));
        if (key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new InvalidOperationException("Processor signing requires ECDSA P-256.");
        byte[] payload = BackendMessageSignaturePayload.CreateForProcessor(operation, target, message);
        // The ESP32 accepts a 384 KiB MQTT command and at most 256 KiB of signed
        // bytes. Base64 expands 256 KiB to ~342 KiB, leaving room for the
        // signature and CloudEvent envelope. Keep this in sync with the device.
        if (payload.Length > 256 * 1024) throw new InvalidOperationException("Signed processor command exceeds device limit.");
        return new ProcessorSignedCommand {
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence))
        };
    }
}
