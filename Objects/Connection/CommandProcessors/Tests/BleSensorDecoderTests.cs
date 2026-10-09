using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests;

public class BleSensorDecoderTests
{
    private const string RuuviValid = "0512FC5394C37C0004FFFC040CAC364200CDCBB8334C884F";
    private const string BTHomeEncrypted = "D2FC41E445F3C9962B332211006C7C4519";
    private const string BTHomeKey = "231D39C1D7CC1AB1AEE224CD096DB932";

    [Fact]
    public void RuuviPublishedVectorDecodesWithoutKey()
    {
        string output = Decode(new RuuviPayloadDecoder(), RuuviValid);
        foreach (string reading in new[] { "Temperature: 24.3 °C", "Humidity: 53.49 %", "Pressure: 100044 Pa",
            "Acceleration X: 0.004 g", "Acceleration Y: -0.004 g", "Acceleration Z: 1.036 g",
            "Battery voltage: 2.977 V", "TX power: 4 dBm", "Movement counter: 66", "Measurement sequence: 205", "Device MAC: CB:B8:33:4C:88:4F" })
            Assert.Contains(reading, output);
    }

    [Fact]
    public void RuuviPublishedUnavailableVectorProducesNA()
    {
        string output = Decode(new RuuviPayloadDecoder(), "058000FFFFFFFF800080008000FFFFFFFFFFFFFFFFFFFFFF");
        Assert.Contains("Temperature: NA", output);
        Assert.Contains("Humidity: NA", output);
        Assert.Contains("Battery voltage: NA", output);
        Assert.Contains("TX power: NA", output);
        Assert.Contains("Device MAC: NA", output);
    }

    [Theory]
    [InlineData("057FFFFFFEFFFE7FFF7FFF7FFFFFDEFEFFFECBB8334C884F", "Temperature: 163.835 °C", "Battery voltage: 3.646 V")]
    [InlineData("058001000000008001800180010000000000CBB8334C884F", "Temperature: -163.835 °C", "Battery voltage: 1.6 V")]
    public void RuuviPublishedBoundaryVectors(string hex, string temperature, string voltage)
    {
        string output = Decode(new RuuviPayloadDecoder(), hex);
        Assert.Contains(temperature, output);
        Assert.Contains(voltage, output);
    }

    [Fact]
    public void RuuviSelectsItsManufacturerBlockAndRejectsInvalidFormats()
    {
        byte[] record = Convert.FromHexString("9904" + RuuviValid);
        byte[] raw = new byte[] { 3, 0xff, 0x4c, 0, (byte)(record.Length + 1), 0xff }.Concat(record).ToArray();
        var decoder = new RuuviPayloadDecoder();
        Assert.True(decoder.Accepts(raw, "raw", 0));
        Assert.True(decoder.TryDecode(new BlePayload("device", "raw", raw), Array.Empty<byte>(), out var output, out _));
        Assert.Contains("Temperature: 24.3 °C", output);
        Assert.False(decoder.Accepts(Convert.FromHexString("4C00" + RuuviValid), "manufacturer", 0));
        Assert.False(decoder.Accepts(new byte[] { 5 }, "raw_input", 0));
        Assert.False(decoder.Accepts(Convert.FromHexString("06" + RuuviValid[2..]), "raw_input", 0));
    }

    [Fact]
    public void BTHomePublishedUnencryptedAdvertisementDecodes()
    {
        byte[] raw = Convert.FromHexString("0201060B094449592D73656E736F720A16D2FC4002C40903BF13");
        var decoder = new BTHomePayloadDecoder();
        Assert.True(decoder.Accepts(raw, "raw", 0));
        Assert.True(decoder.TryDecode(new BlePayload("device", "raw", raw), Array.Empty<byte>(), out var output, out var error), error);
        Assert.Contains("Temperature: 25 °C", output);
        Assert.Contains("Humidity: 50.55 %", output);
        Assert.Contains("Encrypted: no", output);
        Assert.Equal("service", decoder.DefaultPayloadMode);
        Assert.Equal("0000fcd2-0000-1000-8000-00805f9b34fb", decoder.ServiceUuid);
    }

    [Fact]
    public void BTHomePublishedEncryptionVectorAuthenticates()
    {
        string output = Decode(new BTHomePayloadDecoder(), BTHomeEncrypted, BTHomeKey, "54:48:E6:8F:80:A5");
        Assert.Contains("Temperature: 25.06 °C", output);
        Assert.Contains("Humidity: 50.55 %", output);
        Assert.Contains("Encryption counter: 1122867", output);
        Assert.Contains("Encrypted: yes", output);
    }

    [Theory]
    [InlineData("", "54:48:E6:8F:80:A5", "requires a 16-byte")]
    [InlineData("00000000000000000000000000000000", "54:48:E6:8F:80:A5", "authentication failed")]
    [InlineData(BTHomeKey, "54:48:E6:8F:80:A6", "authentication failed")]
    [InlineData(BTHomeKey, "unknown", "MAC address")]
    public void BTHomeEncryptionErrorsHaveNoDecodedReadings(string key, string address, string expected)
    {
        var decoder = new BTHomePayloadDecoder();
        Assert.False(decoder.TryDecode(new BlePayload(address, "raw_input", Convert.FromHexString(BTHomeEncrypted)),
            Convert.FromHexString(key), out var output, out var error));
        Assert.Empty(output);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void BTHomeTamperedCiphertextIsRejected()
    {
        byte[] data = Convert.FromHexString(BTHomeEncrypted);
        data[3] ^= 1;
        Assert.False(new BTHomePayloadDecoder().TryDecode(new BlePayload("54:48:E6:8F:80:A5", "raw_input", data),
            Convert.FromHexString(BTHomeKey), out _, out var error));
        Assert.Contains("authentication failed", error);
    }

    [Fact]
    public void BTHomeSupportsBinaryStatesButtonsRepeatedReadingsAndTriggerFlag()
    {
        string output = Decode(new BTHomePayloadDecoder(), "44016402C409029CFF1A0121003A013A023C020A");
        foreach (string reading in new[] { "Trigger based: yes", "Battery: 100 %", "Temperature: 25 °C", "Temperature 2: -1 °C",
            "Door: on", "Motion: off", "Button: press", "Button 2: double press", "Dimmer: rotate right 10 steps" }) Assert.Contains(reading, output);
    }

    [Theory]
    [InlineData("4004138A01", "Pressure: 1008.83 hPa")]
    [InlineData("400B021B00", "Power: 69.14 W")]
    [InlineData("405C02FBFFFF", "Power: -12.78 W")]
    [InlineData("40634057D0FF", "Acceleration: -3.123392 m/s²")]
    [InlineData("40530548656C6C6F", "Text: Hello")]
    [InlineData("40540200FF", "Raw: 00FF")]
    [InlineData("40F100010204", "Firmware version: 4.2.1.0")]
    [InlineData("403B010305", "Command (opcode/arguments): 0305")]
    public void BTHomeDecodesOtherObjectTypes(string hex, string expected) => Assert.Contains(expected, Decode(new BTHomePayloadDecoder(), hex));

    [Fact]
    public void BTHomeUnknownObjectsStopSafelyAndPreserveEarlierReadings()
    {
        string output = Decode(new BTHomePayloadDecoder(), "4001646602C409");
        Assert.Contains("Battery: 100 %", output);
        Assert.Contains("Unsupported BTHome object: 0x66", output);
        Assert.DoesNotContain("Temperature:", output);
    }

    [Theory]
    [InlineData("4002C4")]
    [InlineData("40530A4869")]
    [InlineData("401A02")]
    [InlineData("403C01")]
    [InlineData("41")]
    [InlineData("200164")]
    public void BTHomeRejectsTruncatedOrInvalidPackets(string hex)
    {
        Assert.False(new BTHomePayloadDecoder().TryDecode(new BlePayload("device", "raw_input", Convert.FromHexString(hex)),
            Array.Empty<byte>(), out var output, out _));
        Assert.Empty(output);
    }

    [Theory]
    [InlineData("ruuvi", "", true)]
    [InlineData("ruuvi", BTHomeKey, false)]
    [InlineData("bthome", "", true)]
    [InlineData("bthome", BTHomeKey, true)]
    [InlineData("bthome", "000000000000000000000000000000000000000000000000", false)]
    [InlineData("victron", "000000000000000000000000000000000000000000000000", false)]
    [InlineData("aesgcm", "000000000000000000000000000000000000000000000000", true)]
    public void KeyRequirementsBelongToProtocol(string format, string key, bool success) =>
        Assert.Equal(success, BleKeyParser.TryParse(key, BlePayloadDecoderRegistry.Default.Find(format), out _, out _));

    [Theory]
    [InlineData(false, "ruuvi", RuuviValid, "", "Temperature: 24.3 °C")]
    [InlineData(true, "ruuvi", RuuviValid, "", "Temperature: 24.3 °C")]
    [InlineData(false, "bthome", "4002C40903BF13", "", "Temperature: 25 °C")]
    [InlineData(true, "bthome", "4002C40903BF13", "", "Temperature: 25 °C")]
    [InlineData(false, "bthome", BTHomeEncrypted, BTHomeKey, "Temperature: 25.06 °C")]
    public async Task CommandProcessorsRouteProtocolsWithOptionalPasswords(bool listen, string format, string hex, string key, string expected)
    {
        var config = new NetConnectConfig(new ConfigurationBuilder().Build(), "TestSection") { OSPlatform = "linux" };
        var states = new LocalCmdProcessorStates("ble", "BLE") { IsCmdAvailable = true };
        using ICmdProcessor processor = listen
            ? new BleBroadcastListenCmdProcessor(NullLogger.Instance, states, Mock.Of<IRabbitRepo>(), config)
            : new BleBroadcastCmdProcessor(NullLogger.Instance, states, Mock.Of<IRabbitRepo>(), config);
        string args = $"--format {format} --raw_payload {hex}";
        if (!listen) args += " --address 54:48:E6:8F:80:A5 --metric temperature --metric_scale 100 --metric_offset 100";
        if (key.Length > 0) args += $" --key {key}";
        var result = await processor.RunCommand(args, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        if (listen)
        {
            Assert.Contains(hex, result.Message);
            Assert.DoesNotContain("Temperature:", result.Message);
        }
        else Assert.Contains(expected, result.Message);
        if (!listen)
        {
            var decoded = Assert.IsType<BleDecodedPayload>(result.Data);
            Assert.True(BleMetricSelector.TrySelect(decoded.Readings, "temperature", 100, 100, out _, out _, out var metricError), metricError);
        }
        Assert.Contains("ruuvi", processor.GetCommandHelp());
        Assert.Contains("bthome", processor.GetCommandHelp());
    }

    private static string Decode(IBlePayloadDecoder decoder, string hex, string key = "", string address = "device")
    {
        Assert.True(decoder.TryDecode(new BlePayload(address, "raw_input", Convert.FromHexString(hex)),
            Convert.FromHexString(key), out var output, out var error), error);
        return output;
    }
}
