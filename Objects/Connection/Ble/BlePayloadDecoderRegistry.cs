using System;
using System.Collections.Generic;

namespace NetworkMonitor.Connection;

/// <summary>Payload selected from an advertisement. Manufacturer payloads retain their company ID.</summary>
public sealed record BlePayload(string Address, string PayloadType, byte[] Payload);

/// <summary>Protocol extension point; implementations own packet recognition, crypto and decoding.</summary>
public interface IBlePayloadDecoder
{
    string Format { get; }
    int? ManufacturerId { get; }
    bool RequiresKey { get; }
    string DefaultPayloadMode => "manufacturer";
    string? ServiceUuid => null;
    string? GetKeyError(byte[] key) => BleKeyParser.GetAesKeyError(key);
    bool Accepts(byte[] payload, string payloadType, byte keyFirstByte);
    string Describe(byte[] payload, string payloadType);
    bool TryDecode(BlePayload payload, byte[] key, out string message, out string error);
    bool TryDecodeReadings(BlePayload payload, byte[] key, out BleDecodedPayload decoded, out string error)
    {
        bool success = TryDecode(payload, key, out var message, out error);
        decoded = new BleDecodedPayload(message, Array.Empty<BleReading>());
        return success;
    }
}

/// <summary>Immutable protocol catalogue. Add protocols here rather than in platform scanners.</summary>
public sealed class BlePayloadDecoderRegistry
{
    public static BlePayloadDecoderRegistry Default { get; } = new(new IBlePayloadDecoder[]
    {
        new VictronPayloadDecoder(),
        new RuuviPayloadDecoder(),
        new BTHomePayloadDecoder()
    });

    private readonly Dictionary<string, IBlePayloadDecoder> _decoders = new(StringComparer.OrdinalIgnoreCase);

    public BlePayloadDecoderRegistry(IEnumerable<IBlePayloadDecoder> decoders)
    {
        foreach (var decoder in decoders)
        {
            if (string.IsNullOrWhiteSpace(decoder.Format) || !_decoders.TryAdd(decoder.Format, decoder))
                throw new ArgumentException("BLE decoder formats must be nonempty and unique.", nameof(decoders));
        }
    }

    public IBlePayloadDecoder? Find(string format) =>
        _decoders.TryGetValue(format.Trim(), out var decoder) ? decoder : null;
}
