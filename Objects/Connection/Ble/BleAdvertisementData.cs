using System.Buffers.Binary;

namespace NetworkMonitor.Connection;

/// <summary>Normalizes complete AD records and platform-selected payloads for protocol decoders.</summary>
internal static class BleAdvertisementData
{
    public static bool TrySelect(BlePayload capture, ushort identifier, bool service, out byte[] data, out string error)
    {
        data = Array.Empty<byte>();
        error = "";
        ReadOnlySpan<byte> bytes = capture.Payload;
        if (capture.PayloadType.Equals("raw", StringComparison.OrdinalIgnoreCase))
        {
            for (int offset = 0; offset < bytes.Length;)
            {
                int length = bytes[offset];
                if (length == 0) break;
                if (length < 1 || offset + length + 1 > bytes.Length)
                {
                    error = "Malformed BLE advertisement.";
                    return false;
                }
                var block = bytes.Slice(offset + 2, length - 1);
                if (bytes[offset + 1] == (service ? 0x16 : 0xff) && block.Length >= 2
                    && BinaryPrimitives.ReadUInt16LittleEndian(block) == identifier)
                {
                    data = block[2..].ToArray();
                    return true;
                }
                offset += length + 1;
            }
            error = $"Advertisement contains no {(service ? "service" : "manufacturer")} data for 0x{identifier:X4}.";
            return false;
        }
        bool hasIdentifier = bytes.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(bytes) == identifier;
        if (!service && capture.PayloadType.Equals("manufacturer", StringComparison.OrdinalIgnoreCase) && !hasIdentifier)
        {
            error = $"Manufacturer data does not belong to 0x{identifier:X4}.";
            return false;
        }
        if (hasIdentifier) bytes = bytes[2..];
        data = bytes.ToArray();
        return true;
    }
}
