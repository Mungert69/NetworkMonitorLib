using System.Globalization;
using System.Text;

namespace NetworkMonitor.Connection;

/// <summary>
/// Little-endian bit layouts from Victron's Extra Manufacturer Data specification (2022-12-14).
/// Offsets below are relative to decrypted data, excluding the 32-bit record header.
/// Orion XS follows the layout published by Victron staff in September 2024.
/// </summary>
public static class VictronDeviceRecordDecoders
{
    public static IEnumerable<IVictronRecordDecoder> CreateDefaults()
    {
        yield return new VictronSolarChargerRecordDecoder();
        yield return new VictronLayoutRecordDecoder(0x02, "Battery monitor", 118, new[]
        {
            F("Time to go",0,16,unit:"min",na:0xffff), V(16), F("Alarm reason",32,16),
            F("Aux input",64,2,na:3), F("Battery current",66,22,true,0.001,"A",0x3fffff),
            F("Consumed Ah",88,20,scale:-0.1,unit:"Ah",na:0xfffff), F("State of charge",108,10,scale:0.1,unit:"%",na:0x3ff)
        }, AppendAux);
        yield return new VictronLayoutRecordDecoder(0x03, "Inverter", 82, new[]
        {
            State(), F("Alarm reason",8,16), V(24), F("AC apparent power",40,16,unit:"VA",na:0xffff),
            F("AC voltage",56,15,scale:0.01,unit:"V",na:0x7fff), F("AC current",71,11,scale:0.1,unit:"A",na:0x7ff)
        });
        yield return new VictronLayoutRecordDecoder(0x04, "DC/DC converter", 80, new[]
        {
            State(), Error(), F("Input voltage",16,16,scale:0.01,unit:"V",na:0xffff), V(32,"Output voltage"), F("Off reason",48,32)
        });
        var lithium = new List<VictronField> { F("BMS flags", 0, 32), F("SmartLithium error", 32, 16) };
        for (int cell = 0; cell < 8; cell++)
            lithium.Add(F($"Cell {cell + 1} voltage", 48 + 7 * cell, 7, scale: 0.01, unit: "V", na: 0x7f, offset: 2.60, cellVoltage: true));
        lithium.AddRange(new[] { F("Battery voltage", 104, 12, scale: 0.01, unit: "V", na: 0xfff), F("Balancer status", 116, 4, na: 15), Temp(120) });
        yield return new VictronLayoutRecordDecoder(0x05, "SmartLithium", 127, lithium);
        yield return new VictronLayoutRecordDecoder(0x06, "Inverter RS", 96, new[]
        {
            State(), Error(), V(16), I(32), F("PV power",48,16,unit:"W",na:0xffff),
            F("Yield today",64,16,scale:0.01,unit:"kWh",na:0xffff), F("AC out power",80,16,true,unit:"W",na:0x7fff)
        });
        var ac = new List<VictronField> { State(), Error() };
        for (int channel = 0; channel < 3; channel++)
        {
            ac.Add(F($"Battery voltage {channel + 1}", 16 + 24 * channel, 13, scale: 0.01, unit: "V", na: 0x1fff));
            ac.Add(F($"Battery current {channel + 1}", 29 + 24 * channel, 11, scale: 0.1, unit: "A", na: 0x7ff));
        }
        ac.AddRange(new[] { Temp(88), F("AC current", 95, 9, scale: 0.1, unit: "A", na: 0x1ff) });
        yield return new VictronLayoutRecordDecoder(0x08, "AC charger", 104, ac);
        // The PDF's Battery Protect start-bit column is inconsistent with its common header.
        // Its payload starts with device state, output state, error (confirmed by victron-ble).
        yield return new VictronLayoutRecordDecoder(0x09, "Smart Battery Protect", 120, new[]
        {
            State(), F("Output state",8,8,na:0xff), F("Error code",16,8,na:0xff), F("Alarm reason",24,16),
            F("Warning reason",40,16), V(56,"Input voltage"), F("Output voltage",72,16,scale:0.01,unit:"V",na:0xffff), F("Off reason",88,32)
        });
        yield return new VictronLayoutRecordDecoder(0x0a, "Lynx Smart BMS", 127, new[]
        {
            F("BMS error",0,8), F("Time to go",8,16,unit:"min",na:0xffff), V(24), I(40),
            F("IO status",56,16), F("Warnings/alarms",72,18), F("State of charge",90,10,scale:0.1,unit:"%",na:0x3ff),
            F("Consumed Ah",100,20,scale:-0.1,unit:"Ah",na:0xfffff), Temp(120)
        });
        yield return new VictronLayoutRecordDecoder(0x0b, "Multi RS", 112, new[]
        {
            State(), Error(), I(16), F("Battery voltage",32,14,scale:0.01,unit:"V",na:0x3fff), F("Active AC input",46,2,na:3),
            F("AC in power",48,16,true,unit:"W",na:0x7fff), F("AC out power",64,16,true,unit:"W",na:0x7fff),
            F("PV power",80,16,unit:"W",na:0xffff), F("Yield today",96,16,scale:0.01,unit:"kWh",na:0xffff)
        });
        yield return new VictronLayoutRecordDecoder(0x0c, "VE.Bus", 102, new[]
        {
            State(), F("VE.Bus error",8,8,na:0xff), I(16), F("Battery voltage",32,14,scale:0.01,unit:"V",na:0x3fff),
            F("Active AC input",46,2,na:3), F("AC in power",48,19,true,unit:"W",na:0x3ffff),
            F("AC out power",67,19,true,unit:"W",na:0x3ffff), F("Alarm",86,2,na:3), Temp(88), F("State of charge",95,7,unit:"%",na:0x7f)
        });
        yield return new VictronLayoutRecordDecoder(0x0d, "DC energy meter", 88, new[]
        {
            F("Monitor mode",0,16,true), V(16), F("Alarm reason",32,16), F("Aux input",64,2,na:3),
            F("Battery current",66,22,true,0.001,"A",0x3fffff)
        }, AppendAux);
        yield return new VictronLayoutRecordDecoder(0x0f, "Orion XS", 112, new[]
        {
            State(), Error(), V(16,"Output voltage"), I(32,"Output current"),
            F("Input voltage",48,16,scale:0.01,unit:"V",na:0xffff), F("Input current",64,16,scale:0.1,unit:"A",na:0xffff), F("Off reason",80,32)
        });
    }

    private static VictronField F(string label, int bit, int width, bool signed = false, double scale = 1, string unit = "", uint? na = null, double offset = 0, bool cellVoltage = false)
        => new(label, bit, width, signed, scale, unit, na, offset, cellVoltage);
    private static VictronField State() => F("Device state", 0, 8, na: 0xff);
    private static VictronField Error() => F("Charger error", 8, 8, na: 0xff);
    private static VictronField V(int bit, string label = "Battery voltage") => F(label, bit, 16, true, 0.01, "V", 0x7fff);
    private static VictronField I(int bit, string label = "Battery current") => F(label, bit, 16, true, 0.1, "A", 0x7fff);
    private static VictronField Temp(int bit) => F("Battery temperature", bit, 7, unit: "°C", na: 0x7f, offset: -40);

    private static void AppendAux(byte[] data, StringBuilder output)
    {
        uint mode = VictronBits.Read(data, 64, 2);
        VictronField? field = mode switch
        {
            0 => F("Aux voltage", 48, 16, true, 0.01, "V"),
            1 => F("Mid voltage", 48, 16, scale: 0.01, unit: "V"),
            2 => F("Battery temperature", 48, 16, scale: 0.01, unit: "°C", offset: -273.15),
            _ => null
        };
        field?.Append(data, output);
    }
}

internal static class VictronBits
{
    public static uint Read(byte[] data, int bit, int width)
    {
        uint value = 0;
        for (int i = 0; i < width; i++)
            value |= (uint)((data[(bit + i) / 8] >> ((bit + i) % 8)) & 1) << i;
        return value;
    }
}

internal sealed record VictronField(string Label, int Bit, int Width, bool Signed, double Scale, string Unit, uint? Na, double Offset, bool CellVoltage = false)
{
    public void Append(byte[] data, StringBuilder output)
    {
        uint raw = VictronBits.Read(data, Bit, Width);
        if (raw == Na) { output.AppendLine($"{Label}: NA"); return; }
        if (CellVoltage && raw is 0 or 126)
        {
            output.AppendLine($"{Label}: {(raw == 0 ? "<2.61" : ">3.85")} V");
            return;
        }
        long number = Signed && (raw & (1u << (Width - 1))) != 0 ? (long)raw - (1L << Width) : raw;
        string format = Scale switch { 0.001 => "F3", 0.01 => "F2", 0.1 or -0.1 => "F1", _ => "0.##" };
        output.AppendLine($"{Label}: {(number * Scale + Offset).ToString(format, CultureInfo.InvariantCulture)}{(Unit.Length == 0 ? "" : " " + Unit)}");
    }
}

internal sealed class VictronLayoutRecordDecoder(byte recordType, string name, int requiredBits,
    IEnumerable<VictronField> fields, Action<byte[], StringBuilder>? appendExtra = null) : IVictronRecordDecoder
{
    private readonly VictronField[] _fields = fields.ToArray();
    public byte RecordType => recordType;
    public bool TryAppend(byte[] plaintext, StringBuilder output, out string error)
    {
        error = "";
        if (plaintext.Length * 8 < requiredBits)
        {
            error = $"Victron {name} payload too short ({plaintext.Length}; expected at least {(requiredBits + 7) / 8} bytes).";
            return false;
        }
        output.AppendLine($"Victron device: {name}");
        foreach (var field in _fields) field.Append(plaintext, output);
        appendExtra?.Invoke(plaintext, output);
        return true;
    }
}
