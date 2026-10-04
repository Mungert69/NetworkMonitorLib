using System.Text;

namespace NetworkMonitor.Connection;

internal static class BleKeyParser
{
    public static bool TryParse(string input, IBlePayloadDecoder? decoder, out byte[] key, out string error)
    {
        key = Array.Empty<byte>();
        error = "";
        if (!string.IsNullOrWhiteSpace(input))
        {
            string text = input.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (text.Length > 0 && text.Length % 2 == 0 && text.All(Uri.IsHexDigit)) key = Convert.FromHexString(text);
            else
            {
                try { key = Convert.FromBase64String(text); }
                catch (FormatException) { key = Encoding.UTF8.GetBytes(text); }
            }
        }
        error = (decoder != null ? decoder.GetKeyError(key) : GetAesKeyError(key)) ?? "";
        return error.Length == 0;
    }

    public static string? GetAesKeyError(byte[] key) => key.Length is 0 or 16 or 24 or 32
        ? null : "Key length must be 16, 24, or 32 bytes (AES-128/192/256).";
}
