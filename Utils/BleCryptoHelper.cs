using System;
using System.Security.Cryptography;

namespace NetworkMonitor.Utils
{
    public static class BleCryptoHelper
    {
        public static string NormalizeFormat(string format, bool hasKey)
        {
            var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "raw";
            }

            if (!hasKey && normalized != "victron")
            {
                return "raw";
            }

            return normalized;
        }

        public static bool TryDecryptPayload(
            string format,
            byte[] payload,
            byte[] key,
            BleCryptoOptions options,
            out byte[] plaintext,
            out string error)
        {
            plaintext = Array.Empty<byte>();
            error = string.Empty;

            if (payload == null || payload.Length == 0)
            {
                error = "Payload is empty.";
                return false;
            }

            format = (format ?? string.Empty).Trim().ToLowerInvariant();

            if (format == "raw")
            {
                plaintext = payload;
                return true;
            }

            if (key == null || key.Length == 0)
            {
                error = "Missing key for decryption.";
                return false;
            }

            return format switch
            {
                "aesgcm" => TryDecryptAesGcm(payload, key, options, out plaintext, out error),
                "aesctr" => TryDecryptAesCtr(payload, key, options, out plaintext, out error),
                _ => FailUnsupported(format, out plaintext, out error)
            };
        }

        private static bool FailUnsupported(string format, out byte[] plaintext, out string error)
        {
            plaintext = Array.Empty<byte>();
            error = $"Unsupported format: {format}.";
            return false;
        }

        private static bool TryDecryptAesGcm(
            byte[] payload,
            byte[] key,
            BleCryptoOptions options,
            out byte[] plaintext,
            out string error)
        {
            plaintext = Array.Empty<byte>();
            error = string.Empty;

            int nonceLen = options.NonceLength;
            int tagLen = options.TagLength;

            if (nonceLen <= 0 || tagLen <= 0)
            {
                error = "Nonce/tag length must be positive.";
                return false;
            }

            if (payload.Length < nonceLen + tagLen + 1)
            {
                error = "Payload is too short for AES-GCM (need nonce + tag + data).";
                return false;
            }

            ReadOnlySpan<byte> nonce;
            ReadOnlySpan<byte> tag;
            ReadOnlySpan<byte> ciphertext;

            if (options.NoncePlacement == BleNoncePlacement.Start)
            {
                nonce = payload.AsSpan(0, nonceLen);
                tag = payload.AsSpan(payload.Length - tagLen, tagLen);
                ciphertext = payload.AsSpan(nonceLen, payload.Length - nonceLen - tagLen);
            }
            else
            {
                nonce = payload.AsSpan(payload.Length - nonceLen, nonceLen);
                tag = payload.AsSpan(payload.Length - nonceLen - tagLen, tagLen);
                ciphertext = payload.AsSpan(0, payload.Length - nonceLen - tagLen);
            }

            if (ciphertext.Length <= 0)
            {
                error = "Payload has no ciphertext after parsing.";
                return false;
            }

            try
            {
                plaintext = new byte[ciphertext.Length];
                using var aes = new AesGcm(key, tagLen);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return true;
            }
            catch (CryptographicException ex)
            {
                error = $"Decryption failed: {ex.Message}";
                return false;
            }
        }

        private static bool TryDecryptAesCtr(
            byte[] payload,
            byte[] key,
            BleCryptoOptions options,
            out byte[] plaintext,
            out string error)
        {
            plaintext = Array.Empty<byte>();
            error = string.Empty;

            int nonceLen = options.NonceLength;
            if (nonceLen <= 0 || nonceLen > payload.Length)
            {
                error = "Invalid nonce length for AES-CTR.";
                return false;
            }

            byte[] nonce;
            ReadOnlySpan<byte> ciphertext;

            if (options.NoncePlacement == BleNoncePlacement.Start)
            {
                nonce = payload.AsSpan(0, nonceLen).ToArray();
                ciphertext = payload.AsSpan(nonceLen);
            }
            else
            {
                nonce = payload.AsSpan(payload.Length - nonceLen, nonceLen).ToArray();
                ciphertext = payload.AsSpan(0, payload.Length - nonceLen);
            }

            if (ciphertext.Length == 0)
            {
                error = "Payload has no ciphertext after parsing.";
                return false;
            }

            try
            {
                plaintext = AesCtrTransform(key, nonce, ciphertext);
                return true;
            }
            catch (CryptographicException ex)
            {
                error = $"Decryption failed: {ex.Message}";
                return false;
            }
        }

        private static byte[] AesCtrTransform(byte[] key, byte[] nonce, ReadOnlySpan<byte> ciphertext)
        {
            byte[] counterBlock = new byte[16];
            int copyLen = Math.Min(nonce.Length, counterBlock.Length);
            Buffer.BlockCopy(nonce, 0, counterBlock, 0, copyLen);

            byte[] output = new byte[ciphertext.Length];
            byte[] keystream = new byte[16];

            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            using var enc = aes.CreateEncryptor();

            int offset = 0;
            while (offset < ciphertext.Length)
            {
                enc.TransformBlock(counterBlock, 0, 16, keystream, 0);
                int blockSize = Math.Min(16, ciphertext.Length - offset);
                for (int i = 0; i < blockSize; i++)
                {
                    output[offset + i] = (byte)(ciphertext[offset + i] ^ keystream[i]);
                }

                IncrementCounter(counterBlock);
                offset += blockSize;
            }

            return output;
        }

        private static void IncrementCounter(byte[] counterBlock)
        {
            for (int i = counterBlock.Length - 1; i >= counterBlock.Length - 4; i--)
            {
                if (++counterBlock[i] != 0)
                {
                    break;
                }
            }
        }
    }
}
