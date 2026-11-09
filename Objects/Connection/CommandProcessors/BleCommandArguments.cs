using NetworkMonitor.Utils;

namespace NetworkMonitor.Connection;

/// <summary>Fresh parser schemas shared by command execution and configuration discovery.</summary>
public static class BleCommandArguments
{
    public static List<ArgSpec> CreateBroadcast() => new()
    {
        new ArgSpec
        {
            Key = "address",
            Required = true,
            IsFlag = false,
            TypeHint = "value",
            Help = "BLE device address (AA:BB:CC:DD:EE:FF)."
        },
        new ArgSpec
        {
            Key = "key",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Protocol encryption key (hex, base64, or raw). Omit for unencrypted packets."
        },
        new ArgSpec
        {
            Key = "format",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            DefaultValue = "aesgcm",
            Help = "Payload format: raw, aesgcm, aesctr, victron, ruuvi, or bthome."
        },
        new ArgSpec
        {
            Key = "nonce_len",
            Required = false,
            IsFlag = false,
            TypeHint = "int",
            DefaultValue = "12",
            Help = "Nonce length for AES-GCM/AES-CTR (default 12)."
        },
        new ArgSpec
        {
            Key = "tag_len",
            Required = false,
            IsFlag = false,
            TypeHint = "int",
            DefaultValue = "16",
            Help = "Tag length for AES-GCM (default 16)."
        },
        new ArgSpec
        {
            Key = "nonce_at",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            DefaultValue = "start",
            Help = "Nonce placement: start or end (default start)."
        },
        new ArgSpec
        {
            Key = "payload",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Payload source: manufacturer, service, or raw. Defaults to the selected protocol."
        },
        new ArgSpec
        {
            Key = "manufacturer_id",
            Required = false,
            IsFlag = false,
            TypeHint = "int",
            DefaultValue = "-1",
            Help = "Manufacturer ID to select specific data (optional)."
        },
        new ArgSpec
        {
            Key = "service_uuid",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Service UUID to select service data (optional)."
        },
        new ArgSpec
        {
            Key = "metric",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Numeric metric name to record (e.g., temperature, state_of_charge, battery_voltage)."
        },
        new ArgSpec
        {
            Key = "metric_scale", Required = false, IsFlag = false, TypeHint = "value",
            Help = "Positive multiplier for the monitor sample; defaults to the metric resolution."
        },
        new ArgSpec
        {
            Key = "metric_offset", Required = false, IsFlag = false, TypeHint = "value",
            Help = "Value added before scaling, allowing signed readings to fit unsigned storage."
        },
        new ArgSpec
        {
            Key = "raw_payload",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Hex payload override (skip scan and decode this payload)."
        }
    };

    public static List<ArgSpec> CreateListen() => new()
    {
        new ArgSpec
        {
            Key = "key",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Legacy option accepted but ignored: listen results are raw and never decrypted."
        },
        new ArgSpec
        {
            Key = "format",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            DefaultValue = "aesgcm",
            Help = "Legacy format option (raw, aesgcm, aesctr, victron, ruuvi, bthome); listen results are always raw."
        },
        new ArgSpec
        {
            Key = "nonce_len",
            Required = false,
            IsFlag = false,
            TypeHint = "int",
            DefaultValue = "12",
            Help = "Legacy crypto option accepted but ignored."
        },
        new ArgSpec
        {
            Key = "tag_len",
            Required = false,
            IsFlag = false,
            TypeHint = "int",
            DefaultValue = "16",
            Help = "Legacy crypto option accepted but ignored."
        },
        new ArgSpec
        {
            Key = "nonce_at",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            DefaultValue = "start",
            Help = "Legacy crypto option accepted but ignored."
        },
        new ArgSpec
        {
            Key = "payload",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Payload source: manufacturer, service, or raw (default raw)."
        },
        new ArgSpec
        {
            Key = "manufacturer_id",
            Required = false,
            IsFlag = false,
            TypeHint = "int",
            DefaultValue = "-1",
            Help = "Manufacturer ID to select specific data (optional)."
        },
        new ArgSpec
        {
            Key = "service_uuid",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Service UUID to select service data (optional)."
        },
        new ArgSpec
        {
            Key = "max_captures",
            Required = false,
            IsFlag = false,
            TypeHint = "int",
            DefaultValue = "10",
            Help = "Legacy option accepted but ignored: all retained advertisements from the cycle are displayed."
        },
        new ArgSpec
        {
            Key = "raw_payload",
            Required = false,
            IsFlag = false,
            TypeHint = "value",
            Help = "Hex payload override for testing raw output."
        }
    };
}
