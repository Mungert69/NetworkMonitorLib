using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NetworkMonitor.Connection;

/// <summary>Victron framing and crypto, shared by targeted and discovery commands.</summary>
public sealed class VictronPayloadDecoder : IBlePayloadDecoder
{
    private readonly Dictionary<byte, IVictronRecordDecoder> _recordDecoders = new();

    public VictronPayloadDecoder() : this(VictronDeviceRecordDecoders.CreateDefaults()) { }

    public VictronPayloadDecoder(IEnumerable<IVictronRecordDecoder> recordDecoders)
    {
        foreach (var decoder in recordDecoders)
            if (!_recordDecoders.TryAdd(decoder.RecordType, decoder))
                throw new ArgumentException("Victron record types must be unique.", nameof(recordDecoders));
    }

    public string Format => "victron";
    public int? ManufacturerId => 0x02E1;
    public bool RequiresKey => true;
    public string? GetKeyError(byte[] key) => key.Length is 0 or 16
        ? null : "Victron decode requires a 16-byte AES-128 key.";
    public bool Accepts(byte[] payload, string payloadType, byte keyFirstByte) =>
        TryExtractVictronRecord(payload, payloadType, out var record, out _)
        && record.Cipher.Length is > 0 and <= 16
        && record.KeyCheck == keyFirstByte && _recordDecoders.ContainsKey(record.RecordType);
    public string Describe(byte[] payload, string payloadType) => DescribeVictronPayload(payload, payloadType);
    public bool TryDecode(BlePayload payload, byte[] key, out string message, out string error) =>
        TryDecodeVictron(payload, key, out message, out error);

    private bool TryDecodeVictron(BlePayload capture, byte[] keyBytes, out string message, out string error)
    {
        message = "";
        error = "";

        if (keyBytes.Length != 16)
        {
            error = "Victron decode requires a 16-byte AES-128 key.";
            return false;
        }

        if (!TryExtractVictronRecord(capture.Payload, capture.PayloadType, out var record, out var extractError))
        {
            error = extractError;
            return false;
        }

        if (record.KeyCheck != keyBytes[0])
        {
            error = $"Victron key check mismatch (recordType=0x{record.RecordType:X2}, nonce=0x{record.Nonce:X4}, header=0x{record.KeyCheck:X2}, key[0]=0x{keyBytes[0]:X2}).";
            return false;
        }

        if (record.Cipher.Length == 0 || record.Cipher.Length > 16)
        {
            error = $"Victron cipher length {record.Cipher.Length} is invalid (expected 1..16).";
            return false;
        }

        byte[] plaintext = DecryptVictronAesCtr(keyBytes, record.Nonce, record.Cipher);

        var sb = new StringBuilder();
        sb.AppendLine($"BLE address: {capture.Address}");
        sb.AppendLine($"Payload ({capture.PayloadType}): {ToHex(capture.Payload)}");
        sb.AppendLine($"Victron recordType: 0x{record.RecordType:X2}");
        sb.AppendLine($"Victron nonce: 0x{record.Nonce:X4}");
        sb.AppendLine($"Victron plaintext: {ToHex(plaintext)}");

        if (_recordDecoders.TryGetValue(record.RecordType, out var deviceDecoder)
            && !deviceDecoder.TryAppend(plaintext, sb, out error))
        {
            return false;
        }

        message = sb.ToString().Trim();
        return true;
    }

    internal struct VictronRecord
    {
        public byte RecordType;
        public ushort Nonce;
        public byte KeyCheck;
        public byte[] Cipher;
    }

    internal static bool TryExtractVictronRecord(byte[] payload, string payloadType, out VictronRecord record, out string error)
    {
        record = default;
        error = "";

        if (payload.Length < 4)
        {
            error = "Victron payload too short.";
            return false;
        }

        ReadOnlySpan<byte> span = payload.AsSpan();

        if (string.Equals(payloadType, "raw", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryExtractManufacturerDataFromRawPayload(payload, out var manufacturerData))
            {
                error = "Advertisement contains no valid Victron manufacturer data.";
                return false;
            }
            span = manufacturerData;
        }
        else if (string.Equals(payloadType, "manufacturer", StringComparison.OrdinalIgnoreCase)
            && (span.Length < 2 || BinaryPrimitives.ReadUInt16LittleEndian(span) != 0x02E1))
        {
            error = "Manufacturer data does not belong to Victron.";
            return false;
        }

        if (span.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(span) == 0x02E1)
        {
            span = span.Slice(2);
        }

        if (span.Length < 4)
        {
            error = "Victron payload too short after company ID.";
            return false;
        }

        int offset;
        if (span[0] == 0x10)
        {
            // Product advertisement record; extra record starts at index 4.
            if (span.Length < 9)
            {
                error = "Victron product advertisement too short.";
                return false;
            }
            offset = 4;
        }
        else
        {
            // Direct extra record starts at index 0.
            offset = 0;
        }

        if (span.Length < offset + 4)
        {
            error = "Victron extra record header missing.";
            return false;
        }

        record.RecordType = span[offset];
        record.Nonce = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset + 1, 2));
        record.KeyCheck = span[offset + 3];
        record.Cipher = span.Slice(offset + 4).ToArray();

        return true;
    }

    internal static bool IsVictronInstantReadout(byte[] payload, string payloadType, byte keyFirstByte) =>
        payload != null && new VictronPayloadDecoder().Accepts(payload, payloadType, keyFirstByte);

    internal static string DescribeVictronPayload(byte[] payload, string payloadType)
    {
        ReadOnlySpan<byte> span = payload.AsSpan();
        if (string.Equals(payloadType, "raw", StringComparison.OrdinalIgnoreCase)
            && TryExtractManufacturerDataFromRawPayload(payload, out var manufacturerData))
        {
            span = manufacturerData;
        }

        if (span.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(span) == 0x02E1)
        {
            span = span.Slice(2);
        }

        if (span.Length == 0)
        {
            return $"payloadType={payloadType}, bytes=0";
        }

        byte packetType = span[0];
        string details = $"payloadType={payloadType}, packetType=0x{packetType:X2}, len={span.Length}";

        if (packetType == 0x10 && span.Length >= 8)
        {
            byte recordType = span[4];
            byte keyCheck = span[7];
            details += $", recordType=0x{recordType:X2}, keyCheck=0x{keyCheck:X2}";
        }
        else if (span.Length >= 4)
        {
            byte keyCheck = span[3];
            details += $", recordType=0x{packetType:X2}, keyCheck=0x{keyCheck:X2}";
        }

        return details;
    }

    internal static bool TryExtractManufacturerDataFromRawPayload(byte[] payload, out ReadOnlySpan<byte> manufacturerData)
    {
        manufacturerData = ReadOnlySpan<byte>.Empty;
        if (payload == null || payload.Length < 3)
        {
            return false;
        }

        int index = 0;
        while (index < payload.Length)
        {
            int length = payload[index];
            if (length == 0)
            {
                break;
            }

            int typeIndex = index + 1;
            if (typeIndex >= payload.Length)
            {
                break;
            }

            byte type = payload[typeIndex];
            int dataIndex = typeIndex + 1;
            int dataLength = length - 1;

            if (dataIndex + dataLength > payload.Length)
            {
                break;
            }

            if (type == 0xFF && dataLength >= 2
                && BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(dataIndex, 2)) == 0x02E1)
            {
                manufacturerData = payload.AsSpan(dataIndex, dataLength);
                return true;
            }

            index += length + 1;
        }

        return false;
    }

    internal static byte[] DecryptVictronAesCtr(byte[] key, ushort nonce, ReadOnlySpan<byte> cipher)
    {
        byte[] counterBlock = new byte[16];
        counterBlock[0] = (byte)(nonce & 0xFF);
        counterBlock[1] = (byte)(nonce >> 8);

        byte[] keystream = new byte[16];
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            using var enc = aes.CreateEncryptor();
            enc.TransformBlock(counterBlock, 0, 16, keystream, 0);
        }

        byte[] plain = new byte[cipher.Length];
        for (int i = 0; i < cipher.Length; i++)
        {
            plain[i] = (byte)(cipher[i] ^ keystream[i]);
        }

        return plain;
    }

    private static string ToHex(byte[] data) => Convert.ToHexString(data);
}
