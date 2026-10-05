using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NetworkMonitor.Connection;

/// <summary>BTHome v2 service-data measurements and events, including AES-CCM authentication.</summary>
public sealed class BTHomePayloadDecoder : IBlePayloadDecoder
{
    private static string Label(ObjectSpec spec) => spec.Label is "Mass" or "Distance" or "Volume"
        ? spec.Label + " " + spec.Unit : spec.Label;
    public static IReadOnlyList<BleMetricRange> Metrics => Objects.Where(p => p.Key is not 0xf1 and not 0xf2)
        .Select(p => BleMetricRange.Field(Label(p.Value), p.Value.Unit,
            p.Value.Binary ? 1 : p.Value.Width * 8, p.Value.Signed, p.Value.Scale,
            kind: p.Value.Binary ? "state" : p.Value.Kind))
        .Concat(new[] {
            BleMetricRange.Field("Button", "", 8, false, kind: "event"),
            BleMetricRange.Field("Dimmer", "", 8, false, kind: "event"),
            BleMetricRange.Field("Dimmer steps", "steps", 8, false, kind: "event") }).ToArray();
    public string Format => "bthome";
    public int? ManufacturerId => null;
    public bool RequiresKey => false; // Encryption is announced by each packet, not required by the protocol.
    public string DefaultPayloadMode => "service";
    public string ServiceUuid => "0000fcd2-0000-1000-8000-00805f9b34fb";
    public string? GetKeyError(byte[] key) => key.Length is 0 or 16 ? null : "BTHome encryption requires a 16-byte AES-128 key.";
    public bool Accepts(byte[] payload, string payloadType, byte keyFirstByte) =>
        BleAdvertisementData.TrySelect(new BlePayload("", payloadType, payload), 0xfcd2, true, out var data, out _)
        && data.Length >= 1 && data[0] >> 5 == 2;
    public string Describe(byte[] payload, string payloadType) => $"BTHome payloadType={payloadType}, bytes={payload.Length}";

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
        if (error.Length > 0) return false;
        if (!BleAdvertisementData.TrySelect(capture, 0xfcd2, true, out var data, out error)) return false;
        if (data.Length < 1 || data[0] >> 5 != 2)
        {
            error = "BTHome decoder requires a v2 device-information byte.";
            return false;
        }
        bool encrypted = (data[0] & 1) != 0;
        byte[] objects;
        uint? counter = null;
        if (encrypted)
        {
            if (key.Length != 16) { error = "Encrypted BTHome packet requires a 16-byte AES-128 key."; return false; }
            if (data.Length < 9) { error = "Encrypted BTHome packet is missing its counter or authentication tag."; return false; }
            if (!TryAddress(capture.Address, out var address))
            {
                error = "Encrypted BTHome requires the device's six-byte BLE MAC address.";
                return false;
            }
            byte[] nonce = new byte[13];
            address.CopyTo(nonce, 0);
            nonce[6] = 0xd2; nonce[7] = 0xfc; nonce[8] = data[0];
            data.AsSpan(data.Length - 8, 4).CopyTo(nonce.AsSpan(9));
            counter = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(nonce.AsSpan(9));
            objects = new byte[data.Length - 9];
            try
            {
                using var aes = new AesCcm(key);
                aes.Decrypt(nonce, data.AsSpan(1, objects.Length), data.AsSpan(data.Length - 4), objects);
            }
            catch (CryptographicException)
            {
                error = "BTHome authentication failed: check the key, device address, and packet integrity.";
                return false;
            }
        }
        else objects = data.AsSpan(1).ToArray();

        var output = new StringBuilder();
        output.AppendLine($"BLE address: {capture.Address}");
        output.AppendLine("BLE protocol: BTHome v2");
        output.AppendLine($"Encrypted: {(encrypted ? "yes" : "no")}");
        output.AppendLine($"Trigger based: {((data[0] & 4) != 0 ? "yes" : "no")}");
        if (counter.HasValue) output.AppendLine($"Encryption counter: {counter}");
        if (!TryAppendObjects(objects, output, readings, out error)) return false;
        message = output.ToString().Trim();
        return true;
    }

    private static bool TryAddress(string address, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        string text = address.Replace(":", "").Replace("-", "");
        if (text.Length != 12 || !text.All(Uri.IsHexDigit)) return false;
        bytes = Convert.FromHexString(text); // BTHome nonce uses MAC bytes in display order.
        return true;
    }

    private static bool TryAppendObjects(byte[] data, StringBuilder output, List<BleReading> readings, out string error)
    {
        error = "";
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int offset = 0; offset < data.Length;)
        {
            byte id = data[offset++];
            if (id is 0x53 or 0x54 or 0x3b)
            {
                if (offset >= data.Length) return Truncated(id, out error);
                int length = data[offset++];
                if (id == 0x3b)
                {
                    if ((length & 0xe0) != 0) { error = "BTHome command uses unsupported reserved length bits."; return false; }
                    length++; // argument length excludes opcode
                }
                if (offset + length > data.Length) return Truncated(id, out error);
                var bytes = data.AsSpan(offset, length);
                string value;
                if (id == 0x53)
                {
                    try { value = new UTF8Encoding(false, true).GetString(bytes); }
                    catch (DecoderFallbackException) { error = "BTHome text contains invalid UTF-8."; return false; }
                }
                else value = Convert.ToHexString(bytes);
                Append(id == 0x53 ? "Text" : id == 0x54 ? "Raw" : "Command (opcode/arguments)", value);
                offset += length;
                continue;
            }
            if (id == 0x3a)
            {
                if (offset >= data.Length) return Truncated(id, out error);
                byte value = data[offset++];
                Append("Button", value switch
                {
                    0 => "none",
                    1 => "press",
                    2 => "double press",
                    3 => "triple press",
                    4 => "long press",
                    5 => "long double press",
                    6 => "long triple press",
                    0x80 => "hold press",
                    _ => $"unknown event 0x{value:X2}"
                }, value);
                continue;
            }
            if (id == 0x3c)
            {
                if (offset + 2 > data.Length) return Truncated(id, out error);
                byte value = data[offset++], steps = data[offset++];
                int dimmerCount = occurrences.GetValueOrDefault("Dimmer") + 1;
                readings.Add(new BleReading("dimmer_steps" + (dimmerCount > 1 ? "_" + dimmerCount : ""), steps, "steps"));
                Append("Dimmer", value switch { 0 => "none", 1 => $"rotate left {steps} steps", 2 => $"rotate right {steps} steps", _ => $"unknown event 0x{value:X2} ({steps} steps)" }, value);
                continue;
            }
            if (!Objects.TryGetValue(id, out var spec))
            {
                // Unknown objects have no discoverable length. Preserve previous readings and stop.
                output.AppendLine($"Unsupported BTHome object: 0x{id:X2}; remaining objects were not decoded.");
                return true;
            }
            if (offset + spec.Width > data.Length) return Truncated(id, out error);
            uint raw = 0;
            for (int i = 0; i < spec.Width; i++) raw |= (uint)data[offset + i] << (8 * i);
            long valueNumber = spec.Signed && (raw & (1u << (spec.Width * 8 - 1))) != 0
                ? (long)raw - (1L << (spec.Width * 8)) : raw;
            string formatted;
            if (spec.Binary)
            {
                if (raw > 1) { error = $"Invalid BTHome binary value {raw} for object 0x{id:X2}."; return false; }
                formatted = raw == 1 ? "on" : "off";
            }
            else if (id == 0x50) formatted = DateTimeOffset.FromUnixTimeSeconds(raw).ToString("O", CultureInfo.InvariantCulture);
            else if (id is 0xf1 or 0xf2) formatted = string.Join(".", data.AsSpan(offset, spec.Width).ToArray().Reverse());
            else formatted = (valueNumber * spec.Scale).ToString("0.######", CultureInfo.InvariantCulture) + (spec.Unit.Length == 0 ? "" : " " + spec.Unit);
            Append(Label(spec), formatted, id is 0xf1 or 0xf2 ? null : valueNumber * spec.Scale, spec.Unit, spec.Scale);
            offset += spec.Width;
        }
        return true;

        void Append(string label, string value, double? number = null, string unit = "", double resolution = 1)
        {
            int count = occurrences.GetValueOrDefault(label) + 1;
            occurrences[label] = count;
            if (number.HasValue) readings.Add(new BleReading(BleReading.MetricName(label) + (count > 1 ? "_" + count : ""), number, unit, resolution));
            output.AppendLine($"{label}{(count > 1 ? $" {count}" : "")}: {value}");
        }
    }

    private static bool Truncated(byte id, out string error)
    {
        error = $"Truncated BTHome object 0x{id:X2}.";
        return false;
    }

    private sealed record ObjectSpec(string Label, int Width, bool Signed = false, double Scale = 1, string Unit = "", bool Binary = false, string Kind = "continuous");
    private static readonly IReadOnlyDictionary<byte, ObjectSpec> Objects = CreateObjects();
    private static IReadOnlyDictionary<byte, ObjectSpec> CreateObjects()
    {
        var items = new Dictionary<byte, ObjectSpec>
        {
            [0x00] = new("Packet id", 1, Kind: "sequence"),
            [0x01] = new("Battery", 1, Unit: "%"),
            [0x02] = new("Temperature", 2, true, 0.01, "°C"),
            [0x03] = new("Humidity", 2, Scale: 0.01, Unit: "%"),
            [0x04] = new("Pressure", 3, Scale: 0.01, Unit: "hPa"),
            [0x05] = new("Illuminance", 3, Scale: 0.01, Unit: "lx"),
            [0x06] = new("Mass", 2, Scale: 0.01, Unit: "kg"),
            [0x07] = new("Mass", 2, Scale: 0.01, Unit: "lb"),
            [0x08] = new("Dewpoint", 2, true, 0.01, "°C"),
            [0x09] = new("Count", 1, Kind: "counter"),
            [0x0a] = new("Energy", 3, Scale: 0.001, Unit: "kWh", Kind: "counter"),
            [0x0b] = new("Power", 3, Scale: 0.01, Unit: "W"),
            [0x0c] = new("Voltage", 2, Scale: 0.001, Unit: "V"),
            [0x0d] = new("PM2.5", 2, Unit: "µg/m³"),
            [0x0e] = new("PM10", 2, Unit: "µg/m³"),
            [0x12] = new("CO2", 2, Unit: "ppm"),
            [0x13] = new("TVOC", 2, Unit: "µg/m³"),
            [0x14] = new("Moisture", 2, Scale: 0.01, Unit: "%"),
            [0x2e] = new("Humidity", 1, Unit: "%"),
            [0x2f] = new("Moisture", 1, Unit: "%"),
            [0x3d] = new("Count", 2, Kind: "counter"),
            [0x3e] = new("Count", 4, Kind: "counter"),
            [0x3f] = new("Rotation", 2, true, 0.1, "°"),
            [0x40] = new("Distance", 2, Unit: "mm"),
            [0x41] = new("Distance", 2, Scale: 0.1, Unit: "m"),
            [0x42] = new("Duration", 3, Scale: 0.001, Unit: "s"),
            [0x43] = new("Current", 2, Scale: 0.001, Unit: "A"),
            [0x44] = new("Speed", 2, Scale: 0.01, Unit: "m/s"),
            [0x45] = new("Temperature", 2, true, 0.1, "°C"),
            [0x46] = new("UV index", 1, Scale: 0.1),
            [0x47] = new("Volume", 2, Scale: 0.1, Unit: "L"),
            [0x48] = new("Volume", 2, Unit: "mL"),
            [0x49] = new("Volume flow rate", 2, Scale: 0.001, Unit: "m³/hr"),
            [0x4a] = new("Voltage", 2, Scale: 0.1, Unit: "V"),
            [0x4b] = new("Gas", 3, Scale: 0.001, Unit: "m³", Kind: "counter"),
            [0x4c] = new("Gas", 4, Scale: 0.001, Unit: "m³", Kind: "counter"),
            [0x4d] = new("Energy", 4, Scale: 0.001, Unit: "kWh", Kind: "counter"),
            [0x4e] = new("Volume", 4, Scale: 0.001, Unit: "L"),
            [0x4f] = new("Water", 4, Scale: 0.001, Unit: "L", Kind: "counter"),
            [0x50] = new("Timestamp", 4, Kind: "timestamp"),
            [0x51] = new("Acceleration", 2, Scale: 0.001, Unit: "m/s²"),
            [0x52] = new("Gyroscope", 2, Scale: 0.001, Unit: "°/s"),
            [0x55] = new("Volume storage", 4, Scale: 0.001, Unit: "L"),
            [0x56] = new("Conductivity", 2, Unit: "µS/cm"),
            [0x57] = new("Temperature", 1, true, Unit: "°C"),
            [0x58] = new("Temperature", 1, true, 0.35, "°C"),
            [0x59] = new("Count", 1, true, Kind: "counter"),
            [0x5a] = new("Count", 2, true, Kind: "counter"),
            [0x5b] = new("Count", 4, true, Kind: "counter"),
            [0x5c] = new("Power", 4, true, 0.01, "W"),
            [0x5d] = new("Current", 2, true, 0.001, "A"),
            [0x5e] = new("Direction", 2, Scale: 0.01, Unit: "°"),
            [0x5f] = new("Precipitation", 2, Scale: 0.1, Unit: "mm"),
            [0x60] = new("Channel", 1, Kind: "sequence"),
            [0x61] = new("Rotational speed", 2, Unit: "rpm"),
            [0x62] = new("Speed", 4, true, 0.000001, "m/s"),
            [0x63] = new("Acceleration", 4, true, 0.000001, "m/s²"),
            [0x64] = new("Light level", 1),
            [0x65] = new("Settings revision", 1, Kind: "sequence"),
            [0xf0] = new("Device type id", 2, Kind: "sequence"),
            [0xf1] = new("Firmware version", 4),
            [0xf2] = new("Firmware version", 3)
        };
        items[0x0f] = new("Boolean", 1, Binary: true);
        items[0x10] = new("Power state", 1, Binary: true);
        items[0x11] = new("Opening", 1, Binary: true);
        string[] binary = { "Battery low", "Battery charging", "Carbon monoxide", "Cold", "Connectivity", "Door", "Garage door", "Gas detected", "Heat", "Light", "Lock unlocked", "Moisture detected", "Motion", "Moving", "Occupancy", "Plug", "Presence", "Problem", "Running", "Safety", "Smoke", "Sound", "Tamper", "Vibration", "Window" };
        for (int i = 0; i < binary.Length; i++) items[(byte)(0x15 + i)] = new(binary[i], 1, Binary: true);
        return items;
    }
}
