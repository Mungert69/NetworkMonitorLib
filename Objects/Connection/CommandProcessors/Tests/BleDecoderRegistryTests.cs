using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests;

public class BleDecoderRegistryTests
{
    [Fact]
    public void RegistryResolvesFormatsAndRejectsDuplicates()
    {
        var decoder = new VictronPayloadDecoder();
        var registry = new BlePayloadDecoderRegistry(new[] { decoder });
        Assert.Same(decoder, registry.Find(" VICTRON "));
        Assert.Null(registry.Find("unknown"));
        Assert.Throws<ArgumentException>(() => new BlePayloadDecoderRegistry(new[] { decoder, decoder }));
    }

    [Fact]
    public void NewDeviceRecordDecodesWithoutChangingProtocol()
    {
        var device = new TestDeviceDecoder();
        var decoder = new VictronPayloadDecoder(new[] { device });
        byte[] key = new byte[16];
        byte[] counter = new byte[16];
        counter[0] = 0x34;
        counter[1] = 0x12;
        using var aes = Aes.Create();
        aes.Key = key;
        byte[] stream = aes.EncryptEcb(counter, PaddingMode.None);
        byte[] payload = { 0xE1, 0x02, device.RecordType, 0x34, 0x12, 0, (byte)(stream[0] ^ 42) };

        Assert.True(decoder.Accepts(payload, "manufacturer", key[0]));
        Assert.True(decoder.TryDecode(new BlePayload("device", "manufacturer", payload), key,
            out var message, out var error), error);
        Assert.Contains("Test reading: 42", message);
        Assert.Contains("Victron nonce: 0x1234", message);
    }

    [Fact]
    public void SolarDecoderRejectsTruncatedDataAndPreservesMissingLoadCurrent()
    {
        var decoder = new VictronSolarChargerRecordDecoder();
        Assert.False(decoder.TryAppend(new byte[9], new StringBuilder(), out _));
        byte[] plaintext = new byte[12];
        plaintext[10] = 0xff;
        plaintext[11] = 1;
        var output = new StringBuilder();
        Assert.True(decoder.TryAppend(plaintext, output, out _));
        Assert.Contains("Load current: NA", output.ToString());
    }

    private sealed class TestDeviceDecoder : IVictronRecordDecoder
    {
        public byte RecordType => 0x7f;
        public bool TryAppend(byte[] plaintext, StringBuilder output, out string error)
        {
            error = "";
            output.AppendLine($"Test reading: {plaintext[0]}");
            return true;
        }
    }
}
