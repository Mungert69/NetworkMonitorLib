using System;
using System.Text;

namespace NetworkMonitor.Connection;

/// <summary>A device record extension, independent of advertisement framing and encryption.</summary>
public interface IVictronRecordDecoder
{
    byte RecordType { get; }
    bool TryAppend(byte[] plaintext, StringBuilder output, out string error);
}

public sealed class VictronSolarChargerRecordDecoder : IVictronRecordDecoder
{
    public byte RecordType => 0x01;
    public bool TryAppend(byte[] plaintext, StringBuilder sb, out string error)
    {
        error = "";
        if (plaintext.Length < 10)
        {
            error = $"Victron solar payload too short ({plaintext.Length}).";
            return false;
        }

        // Preserve legacy labels while applying the protocol's unavailable-value markers.
        new VictronField("Battery voltage", 16, 16, true, 0.01, "V", 0x7fff, 0).Append(plaintext, sb);
        new VictronField("Battery current", 32, 16, true, 0.1, "A", 0x7fff, 0).Append(plaintext, sb);
        new VictronField("Yield today", 48, 16, false, 0.01, "kWh", 0xffff, 0).Append(plaintext, sb);
        new VictronField("PV power", 64, 16, false, 1, "W", 0xffff, 0).Append(plaintext, sb);
        new VictronField("Device state", 0, 8, false, 1, "", 0xff, 0).Append(plaintext, sb);
        new VictronField("Charger error", 8, 8, false, 1, "", 0xff, 0).Append(plaintext, sb);
        if (plaintext.Length >= 12)
            new VictronField("Load current", 80, 9, false, 0.1, "A", 0x1ff, 0).Append(plaintext, sb);
        return true;
    }
}
