using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests;

public class VictronDeviceRecordDecoderTests
{
    public static TheoryData<byte, int, string> Devices => new()
    {
        {0x01,10,"Battery voltage"}, {0x02,15,"Battery monitor"}, {0x03,11,"Inverter"},
        {0x04,10,"DC/DC converter"}, {0x05,16,"SmartLithium"}, {0x06,12,"Inverter RS"},
        {0x08,13,"AC charger"}, {0x09,15,"Smart Battery Protect"}, {0x0a,16,"Lynx Smart BMS"},
        {0x0b,14,"Multi RS"}, {0x0c,13,"VE.Bus"}, {0x0d,11,"DC energy meter"}, {0x0f,14,"Orion XS"}
    };

    [Theory]
    [MemberData(nameof(Devices))]
    public void DefaultDecoderRecognisesAndDecryptsEachDevice(byte type, int size, string label)
    {
        byte[] plaintext = new byte[size];
        var decoder = new VictronPayloadDecoder();
        byte[] payload = Encrypt(type, plaintext);
        Assert.True(decoder.Accepts(payload, "manufacturer", 0));
        Assert.True(decoder.TryDecode(new BlePayload("device", "manufacturer", payload), new byte[16], out var message, out var error), error);
        Assert.Contains(label, message);
        // Product advertisement framing takes the same record through the shared pipeline.
        byte[] product = payload.Take(2).Concat(new byte[] { 0x10, 0, 0, 0 }).Concat(payload.Skip(2)).ToArray();
        Assert.True(decoder.Accepts(product, "manufacturer", 0));
        Assert.True(decoder.TryDecode(new BlePayload("device", "manufacturer", product), new byte[16], out var productMessage, out error), error);
        Assert.Contains(label, productMessage);
    }

    [Theory]
    [MemberData(nameof(Devices))]
    public void TruncatedRecordsFailWithoutPartialOutput(byte type, int size, string label)
    {
        var record = VictronDeviceRecordDecoders.CreateDefaults().Single(d => d.RecordType == type);
        var output = new StringBuilder();
        Assert.False(record.TryAppend(new byte[size - 1], output, out var error));
        Assert.Contains("too short", error);
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void BatteryMonitorDecodesSignedPackedCurrentSocAndAuxTemperature()
    {
        // Independent fixed bit layout: -1.234 A, -12.3 Ah, 75.6%, 25 C.
        // Build packed tail independently of the production bit reader.
        byte[] data = new byte[15];
        Put(data, 0, 16, 60); Put(data, 16, 16, 1234); Put(data, 48, 16, 29815); Put(data, 64, 2, 2);
        Put(data, 66, 22, (1u << 22) - 1234); Put(data, 88, 20, 123); Put(data, 108, 10, 756);
        string output = DecodeRecord(0x02, data);
        Assert.Contains("Battery current: -1.234 A", output);
        Assert.Contains("Consumed Ah: -12.3 Ah", output);
        Assert.Contains("State of charge: 75.6 %", output);
        Assert.Contains("Battery temperature: 25.00 °C", output);
    }

    [Fact]
    public void VebusDecodesSignedNineteenBitPowerAcrossByteBoundaries()
    {
        byte[] data = new byte[13];
        Put(data, 48, 19, (1u << 19) - 1500); Put(data, 67, 19, 2400);
        Put(data, 88, 7, 65); Put(data, 95, 7, 80);
        string output = DecodeRecord(0x0c, data);
        Assert.Contains("AC in power: -1500 W", output);
        Assert.Contains("AC out power: 2400 W", output);
        Assert.Contains("Battery temperature: 25 °C", output);
        Assert.Contains("State of charge: 80 %", output);
    }

    [Fact]
    public void LithiumDecodesCellsAndMissingValues()
    {
        byte[] data = Enumerable.Repeat((byte)0xff, 16).ToArray();
        Put(data, 48, 7, 70); Put(data, 104, 12, 1320); Put(data, 120, 7, 65);
        string output = DecodeRecord(0x05, data);
        Assert.Contains("Cell 1 voltage: 3.30 V", output);
        Assert.Contains("Cell 2 voltage: NA", output);
        Assert.Contains("Battery voltage: 13.20 V", output);
        Assert.Contains("Battery temperature: 25 °C", output);
    }

    [Fact]
    public void LithiumClippedCellsReportBoundsRatherThanExactValues()
    {
        byte[] data = new byte[16];
        Put(data, 55, 7, 126);
        string output = DecodeRecord(0x05, data);
        Assert.Contains("Cell 1 voltage: <2.61 V", output);
        Assert.Contains("Cell 2 voltage: >3.85 V", output);
    }

    [Fact]
    public void SolarUnavailableValuesAreNeverReportedAsMeasurements()
    {
        byte[] data = Convert.FromHexString("FFFF FF7F FF7F FFFF FFFF FFFF".Replace(" ", ""));
        string output = DecodeRecord(0x01, data);
        Assert.Contains("Battery voltage: NA", output);
        Assert.Contains("Battery current: NA", output);
        Assert.Contains("PV power: NA", output);
        Assert.Contains("Load current: NA", output);
    }

    [Fact]
    public void UnknownRecordsRemainPlaintextAndWrongKeysFail()
    {
        var decoder = new VictronPayloadDecoder();
        byte[] payload = Encrypt(0x07, new byte[] { 1, 2, 3 });
        Assert.False(decoder.Accepts(payload, "manufacturer", 0));
        Assert.True(decoder.TryDecode(new BlePayload("device", "manufacturer", payload), new byte[16], out var output, out _));
        Assert.Contains("Victron plaintext: 010203", output);
        byte[] key = new byte[16]; key[0] = 1;
        Assert.False(decoder.TryDecode(new BlePayload("device", "manufacturer", payload), key, out _, out var error));
        Assert.Contains("key check mismatch", error);
    }

    [Theory]
    [InlineData(0x03, "093412D2045802D8D90700", "Battery voltage: 12.34 V|AC apparent power: 600 VA|AC voltage: 230.00 V|AC current: 1.5 A")]
    [InlineData(0x04, "03002805D5FD00000080", "Input voltage: 13.20 V|Output voltage: -5.55 V|Off reason: 2147483648")]
    [InlineData(0x06, "0000D204E9FFC801D20438FF", "Battery current: -2.3 A|PV power: 456 W|Yield today: 12.34 kWh|AC out power: -200 W")]
    [InlineData(0x08, "000028E5018C2503F06504C10A", "Battery voltage 1: 13.20 V|Battery current 1: 1.5 A|Battery voltage 2: 14.20 V|Battery current 2: 2.5 A|Battery voltage 3: 15.20 V|Battery current 3: 3.5 A|Battery temperature: 25 °C|AC current: 2.1 A")]
    [InlineData(0x09, "0104073412785685FF600900000080", "Device state: 1|Output state: 4|Error code: 7|Alarm reason: 4660|Warning reason: 22136|Input voltage: -1.23 V|Output voltage: 24.00 V|Off reason: 2147483648")]
    [InlineData(0x0a, "053C00D20485FF01800100D2BB070041", "BMS error: 5|Time to go: 60 min|Battery voltage: 12.34 V|Battery current: -12.3 A|IO status: 32769|Warnings/alarms: 131073|State of charge: 75.6 %|Consumed Ah: -12.3 Ah|Battery temperature: 25 °C")]
    [InlineData(0x0b, "000085FFD244D4FEF40158022D00", "Battery current: -12.3 A|Battery voltage: 12.34 V|Active AC input: 1|AC in power: -300 W|AC out power: 500 W|PV power: 600 W|Yield today: 0.45 kWh")]
    [InlineData(0x0d, "F7FFD2040000C409B8ECFF", "Monitor mode: -9|Battery voltage: 12.34 V|Aux voltage: 25.00 V|Battery current: -1.234 A")]
    [InlineData(0x0f, "03002805E0FF78052D0000000080", "Output voltage: 13.20 V|Output current: -3.2 A|Input voltage: 14.00 V|Input current: 4.5 A|Off reason: 2147483648")]
    public void PublishedLayoutsDecodeKnownReadings(byte type, string hex, string expected)
    {
        string output = DecodeRecord(type, Convert.FromHexString(hex));
        foreach (string reading in expected.Split('|')) Assert.Contains(reading, output);
    }

    [Fact]
    public void RawAdvertisementSelectsVictronBlockAndRejectsMalformedOrOtherManufacturers()
    {
        byte[] record = Encrypt(0x04, new byte[10]);
        byte[] advertisement = new byte[] { 3, 0xff, 0x4c, 0x00, (byte)(record.Length + 1), 0xff }.Concat(record).ToArray();
        var decoder = new VictronPayloadDecoder();
        Assert.True(decoder.Accepts(advertisement, "raw", 0));
        Assert.True(decoder.TryDecode(new BlePayload("device", "raw", advertisement), new byte[16], out var output, out _));
        Assert.Contains("DC/DC converter", output);
        record[0] = 0x4c; record[1] = 0;
        Assert.False(decoder.Accepts(record, "manufacturer", 0));
        Assert.False(decoder.Accepts(new byte[] { 30, 0xff, 0xe1, 2 }, "raw", 0));
    }

    [Theory]
    [InlineData(0x02, 15, 16, 16, 0x7fff, "Battery voltage: NA")]
    [InlineData(0x02, 15, 66, 22, 0x3fffff, "Battery current: NA")]
    [InlineData(0x03, 11, 71, 11, 0x7ff, "AC current: NA")]
    [InlineData(0x04, 10, 16, 16, 0xffff, "Input voltage: NA")]
    [InlineData(0x06, 12, 80, 16, 0x7fff, "AC out power: NA")]
    [InlineData(0x08, 13, 29, 11, 0x7ff, "Battery current 1: NA")]
    [InlineData(0x09, 15, 72, 16, 0xffff, "Output voltage: NA")]
    [InlineData(0x0a, 16, 100, 20, 0xfffff, "Consumed Ah: NA")]
    [InlineData(0x0b, 14, 32, 14, 0x3fff, "Battery voltage: NA")]
    [InlineData(0x0c, 13, 48, 19, 0x3ffff, "AC in power: NA")]
    [InlineData(0x0d, 11, 66, 22, 0x3fffff, "Battery current: NA")]
    [InlineData(0x0f, 14, 32, 16, 0x7fff, "Output current: NA")]
    public void UnavailableMarkersAreCheckedBeforeSignedConversion(byte type, int length, int bit, int width, uint marker, string expected)
    {
        byte[] data = new byte[length];
        Put(data, bit, width, marker);
        Assert.Contains(expected, DecodeRecord(type, data));
    }

    [Fact]
    public void AuxiliaryModeSelectsMidpointOrNoReading()
    {
        byte[] data = new byte[15];
        Put(data, 48, 16, 2400); Put(data, 64, 2, 1);
        Assert.Contains("Mid voltage: 24.00 V", DecodeRecord(0x02, data));
        Put(data, 64, 2, 3);
        string output = DecodeRecord(0x02, data);
        Assert.DoesNotContain("Mid voltage:", output);
        Assert.DoesNotContain("Aux voltage:", output);
        Assert.DoesNotContain("Battery temperature:", output);
    }

    private static string DecodeRecord(byte type, byte[] plaintext)
    {
        var decoder = VictronDeviceRecordDecoders.CreateDefaults().Single(d => d.RecordType == type);
        var output = new StringBuilder();
        Assert.True(decoder.TryAppend(plaintext, output, out var error), error);
        return output.ToString();
    }

    private static byte[] Encrypt(byte type, byte[] data)
    {
        using var aes = Aes.Create(); aes.Key = new byte[16];
        byte[] counter = new byte[16]; counter[0] = 0x34; counter[1] = 0x12;
        byte[] stream = aes.EncryptEcb(counter, PaddingMode.None);
        return new byte[] { 0xe1, 0x02, type, 0x34, 0x12, 0 }.Concat(data.Select((v, i) => (byte)(v ^ stream[i]))).ToArray();
    }

    private static void Put(byte[] data, int bit, int width, uint value)
    {
        for (int i = 0; i < width; i++)
        {
            int mask = 1 << ((bit + i) % 8);
            data[(bit + i) / 8] = (byte)((data[(bit + i) / 8] & ~mask) | (((value >> i) & 1) != 0 ? mask : 0));
        }
    }
}
