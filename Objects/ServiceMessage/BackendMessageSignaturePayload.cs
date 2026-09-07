using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Objects.ServiceMessage;

public static class BackendMessageSignaturePayload
{
    public static byte[] Create(string operation, string appId, IBackendSignedMessage message)
    {
        var signature = message.BackendSignature;
        string json;
        try
        {
            message.BackendSignature = string.Empty;
            json = JsonSerializer.Serialize(message, message.GetType(), SourceGenerationContext.Default);
        }
        finally
        {
            message.BackendSignature = signature;
        }

        var operationBytes = Encoding.UTF8.GetBytes(operation ?? string.Empty);
        var appIdBytes = Encoding.UTF8.GetBytes(appId ?? string.Empty);
        var messageBytes = Encoding.UTF8.GetBytes(json);
        var payload = new byte[sizeof(int) * 3 + operationBytes.Length + appIdBytes.Length + messageBytes.Length];
        var offset = 0;
        WritePart(payload, ref offset, operationBytes);
        WritePart(payload, ref offset, appIdBytes);
        WritePart(payload, ref offset, messageBytes);
        return payload;
    }

    private static void WritePart(byte[] destination, ref int offset, byte[] value)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination.AsSpan(offset, sizeof(int)), value.Length);
        offset += sizeof(int);
        value.CopyTo(destination, offset);
        offset += value.Length;
    }
}
