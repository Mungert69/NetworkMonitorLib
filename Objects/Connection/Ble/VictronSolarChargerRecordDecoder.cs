using System;
using System.Text;

namespace NetworkMonitor.Connection;

/// <summary>A device record extension, independent of advertisement framing and encryption.</summary>
public interface IVictronRecordDecoder
{
    byte RecordType { get; }
    IReadOnlyList<BleMetricRange> Metrics => Array.Empty<BleMetricRange>();
    bool TryAppend(byte[] plaintext, StringBuilder output, out string error);
    bool TryAppend(byte[] plaintext, StringBuilder output, ICollection<BleReading> readings, out string error)
        => TryAppend(plaintext, output, out error);
}

public sealed class VictronSolarChargerRecordDecoder : IVictronRecordDecoder
{
    public byte RecordType => 0x01;
    public IReadOnlyList<BleMetricRange> Metrics => new[] {
        BleMetricRange.Field("Battery voltage","V",16,true,.01,na:0x7fff),
        BleMetricRange.Field("Battery current","A",16,true,.1,na:0x7fff),
        BleMetricRange.Field("Yield today","kWh",16,false,.01,na:0xffff,kind:"counter"),
        BleMetricRange.Field("PV power","W",16,false,na:0xffff),
        BleMetricRange.Field("Device state","",8,false,na:0xff,kind:"code"),
        BleMetricRange.Field("Charger error","",8,false,na:0xff,kind:"code"),
        BleMetricRange.Field("Load current","A",9,false,.1,na:0x1ff)
    };
    public bool TryAppend(byte[] plaintext, StringBuilder sb, out string error)
        => TryAppend(plaintext, sb, new List<BleReading>(), out error);
    public bool TryAppend(byte[] plaintext, StringBuilder sb, ICollection<BleReading> readings, out string error)
    {
        error = "";
        if (plaintext.Length < 10)
        {
            error = $"Victron solar payload too short ({plaintext.Length}).";
            return false;
        }

        // Preserve legacy labels while applying the protocol's unavailable-value markers.
        new VictronField("Battery voltage", 16, 16, true, 0.01, "V", 0x7fff, 0).Append(plaintext, sb, readings);
        new VictronField("Battery current", 32, 16, true, 0.1, "A", 0x7fff, 0).Append(plaintext, sb, readings);
        new VictronField("Yield today", 48, 16, false, 0.01, "kWh", 0xffff, 0).Append(plaintext, sb, readings);
        new VictronField("PV power", 64, 16, false, 1, "W", 0xffff, 0).Append(plaintext, sb, readings);
        new VictronField("Device state", 0, 8, false, 1, "", 0xff, 0).Append(plaintext, sb, readings);
        new VictronField("Charger error", 8, 8, false, 1, "", 0xff, 0).Append(plaintext, sb, readings);
        if (plaintext.Length >= 12)
            new VictronField("Load current", 80, 9, false, 0.1, "A", 0x1ff, 0).Append(plaintext, sb, readings);
        return true;
    }
}
