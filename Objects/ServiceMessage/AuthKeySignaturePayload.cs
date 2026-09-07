using System;
using System.Buffers.Binary;
using System.Text;

namespace NetworkMonitor.Objects.ServiceMessage
{
    public static class AuthKeySignaturePayload
    {
        public static byte[] Create(string appId, string authKey)
        {
            var appIdBytes = Encoding.UTF8.GetBytes(appId ?? string.Empty);
            var authKeyBytes = Encoding.UTF8.GetBytes(authKey ?? string.Empty);
            var payload = new byte[sizeof(int) + appIdBytes.Length + sizeof(int) + authKeyBytes.Length];
            var offset = 0;

            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset, sizeof(int)), appIdBytes.Length);
            offset += sizeof(int);
            appIdBytes.CopyTo(payload, offset);
            offset += appIdBytes.Length;
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset, sizeof(int)), authKeyBytes.Length);
            offset += sizeof(int);
            authKeyBytes.CopyTo(payload, offset);

            return payload;
        }
    }
}
