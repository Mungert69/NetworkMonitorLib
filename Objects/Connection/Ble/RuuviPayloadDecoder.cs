using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace NetworkMonitor.Connection;

/// <summary>RuuviTag RAWv2 (format 5), per Ruuvi's published protocol and test vectors.</summary>
public sealed class RuuviPayloadDecoder : IBlePayloadDecoder
{
    public static IReadOnlyList<BleMetricRange> Metrics { get; } = new[] {
        BleMetricRange.Field("Temperature","°C",16,true,.005,na:0x8000),
        BleMetricRange.Field("Humidity","%",16,false,.0025,na:0xffff),
        BleMetricRange.Field("Pressure","Pa",16,false,1,50000,0xffff),
        BleMetricRange.Field("Acceleration X","g",16,true,.001,na:0x8000),
        BleMetricRange.Field("Acceleration Y","g",16,true,.001,na:0x8000),
        BleMetricRange.Field("Acceleration Z","g",16,true,.001,na:0x8000),
        BleMetricRange.Field("Battery voltage","V",11,false,.001,1.6,2047),
        BleMetricRange.Field("TX power","dBm",5,false,2,-40,31),
        BleMetricRange.Field("Movement counter","",8,false,na:255,kind:"counter"),
        BleMetricRange.Field("Measurement sequence","",16,false,na:65535,kind:"sequence")
    };
    public string Format => "ruuvi";
    public int? ManufacturerId => 0x0499;
    public bool RequiresKey => false;
    public string? GetKeyError(byte[] key) => key.Length == 0 ? null : "Ruuvi RAWv2 is unencrypted; leave Password/key empty.";
    public bool Accepts(byte[] payload, string payloadType, byte keyFirstByte) =>
        BleAdvertisementData.TrySelect(new BlePayload("", payloadType, payload), 0x0499, false, out var data, out _)
        && data.Length >= 24 && data[0] == 5;
    public string Describe(byte[] payload, string payloadType) => $"Ruuvi payloadType={payloadType}, bytes={payload.Length}";

    public bool TryDecode(BlePayload capture, byte[] key, out string message, out string error)
    {
        bool ok = TryDecodeReadings(capture, key, out var decoded, out error);
        message = decoded.Message; return ok;
    }
    public bool TryDecodeReadings(BlePayload capture, byte[] key, out BleDecodedPayload decoded, out string error)
    {
        var readings = new List<BleReading>();
        bool ok = TryDecodeCore(capture, key, readings, out var message, out error);
        decoded = new BleDecodedPayload(ok ? message : "", ok ? readings.ToArray() : Array.Empty<BleReading>());
        return ok;
    }
    private bool TryDecodeCore(BlePayload capture, byte[] key, List<BleReading> readings, out string message, out string error)
    {
        message = "";
        error = GetKeyError(key) ?? "";
        if (error.Length != 0) return false;
        if (!BleAdvertisementData.TrySelect(capture, 0x0499, false, out var data, out error)) return false;
        if (data.Length < 24 || data[0] != 5)
        {
            error = "Ruuvi decoder requires a 24-byte RAWv2 (format 5) payload.";
            return false;
        }
        var output = new StringBuilder();
        output.AppendLine($"BLE address: {capture.Address}");
        output.AppendLine("BLE device: RuuviTag (RAWv2)");
        Append("Temperature", Signed(1, 0.005), "°C", 0.005);
        Append("Humidity", Unsigned(3, 0.0025), "%", 0.0025);
        Append("Pressure", Unsigned(5, 1, 50000), "Pa");
        Append("Acceleration X", Signed(7, 0.001), "g", 0.001);
        Append("Acceleration Y", Signed(9, 0.001), "g", 0.001);
        Append("Acceleration Z", Signed(11, 0.001), "g", 0.001);
        ushort power = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(13));
        int battery = power >> 5, tx = power & 31;
        Append("Battery voltage", battery == 2047 ? null : (battery + 1600) / 1000.0, "V", 0.001);
        Append("TX power", tx == 31 ? null : tx * 2 - 40, "dBm");
        Append("Movement counter", data[15] == 255 ? null : data[15], "");
        Append("Measurement sequence", Unsigned(16, 1), "");
        var mac = data.AsSpan(18, 6).ToArray();
        output.AppendLine($"Device MAC: {(mac.All(b => b == 255) ? "NA" : string.Join(":", mac.Select(b => b.ToString("X2"))))}");
        message = output.ToString().Trim();
        return true;

        double? Signed(int offset, double scale)
        {
            short value = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset));
            return value == short.MinValue ? null : value * scale;
        }
        double? Unsigned(int offset, double scale, double add = 0)
        {
            ushort value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
            return value == ushort.MaxValue ? null : value * scale + add;
        }
        void Append(string label, double? value, string unit, double resolution = 1)
        {
            readings.Add(new BleReading(BleReading.MetricName(label), value, unit, resolution));
            output.AppendLine(value.HasValue
                ? $"{label}: {value.Value.ToString("0.####", CultureInfo.InvariantCulture)}{(unit.Length == 0 ? "" : " " + unit)}"
                : $"{label}: NA");
        }
    }
}
