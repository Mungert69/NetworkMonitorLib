using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace NetworkMonitor.Objects.Repository;

public sealed class GradLlmHmacEnvelope
{
    [JsonPropertyName("protocol")] public string Protocol { get; set; } = GradLlmHmacProtocol.Protocol;
    [JsonPropertyName("reply_key")] public string ReplyKey { get; set; } = string.Empty;
    [JsonPropertyName("sequence")] public long Sequence { get; set; }
    [JsonPropertyName("final")] public bool Final { get; set; }
    [JsonPropertyName("payload_base64")] public string PayloadBase64 { get; set; } = string.Empty;
    [JsonPropertyName("signature")] public string Signature { get; set; } = string.Empty;
}

/// <summary>Shared HMAC protocol for the isolated NetworkMonitorML/GradLLM trust domain.</summary>
public sealed class GradLlmHmacProtocol
{
    public const string Protocol = "gradllm-hmac-sha256-v1";
    private readonly byte[] _key;

    public GradLlmHmacProtocol(IConfiguration configuration)
    {
        var key = configuration["GradLlmMessageHmacKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("GradLlmMessageHmacKey must contain at least 32 UTF-8 bytes.");
        _key = Encoding.UTF8.GetBytes(key);
    }

    public GradLlmHmacEnvelope CreateRequest(string exchange, string routingKey, string replyKey, byte[] payload) =>
        Create("request", exchange, routingKey, replyKey, 0, false, payload);

    public bool VerifyResponse(GradLlmHmacEnvelope envelope, string exchange, string replyKey, long expectedSequence)
    {
        if (envelope.Protocol != Protocol || envelope.ReplyKey != replyKey || envelope.Sequence != expectedSequence) return false;
        return Verify(envelope, "response", exchange, replyKey);
    }

    private GradLlmHmacEnvelope Create(string direction, string exchange, string routingKey, string replyKey, long sequence, bool final, byte[] payload)
    {
        var envelope = new GradLlmHmacEnvelope
        {
            ReplyKey = replyKey,
            Sequence = sequence,
            Final = final,
            PayloadBase64 = Convert.ToBase64String(payload)
        };
        envelope.Signature = Sign(direction, exchange, routingKey, envelope);
        return envelope;
    }

    private bool Verify(GradLlmHmacEnvelope envelope, string direction, string exchange, string routingKey)
    {
        byte[] supplied;
        try { supplied = Convert.FromBase64String(envelope.Signature); }
        catch (FormatException) { return false; }
        var expected = Convert.FromBase64String(Sign(direction, exchange, routingKey, envelope));
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private string Sign(string direction, string exchange, string routingKey, GradLlmHmacEnvelope envelope)
    {
        var final = envelope.Final ? "1" : "0";
        var data = string.Join('\n', Protocol, direction, exchange, routingKey, envelope.ReplyKey,
            envelope.Sequence.ToString(CultureInfo.InvariantCulture), final, envelope.PayloadBase64);
        return Convert.ToBase64String(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(data)));
    }
}
