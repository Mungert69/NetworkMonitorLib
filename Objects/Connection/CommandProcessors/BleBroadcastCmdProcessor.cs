using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;

#if ANDROID
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Util;
using Java.Util;
#endif

namespace NetworkMonitor.Connection
{
    public class BleBroadcastCmdProcessor : CmdProcessor
    {
        private readonly List<ArgSpec> _schema;

        private sealed class BleCapture
        {
            public BleCapture(string address, string payloadType, byte[] payload)
            {
                Address = address;
                PayloadType = payloadType;
                Payload = payload;
            }

            public string Address { get; }
            public string PayloadType { get; }
            public byte[] Payload { get; }
        }

        public BleBroadcastCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
            _schema = new()
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
                    Required = true,
                    IsFlag = false,
                    TypeHint = "value",
                    Help = "Encryption key (hex, base64, or raw; 16/24/32 bytes)."
                },
                new ArgSpec
                {
                    Key = "format",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "value",
                    DefaultValue = "aesgcm",
                    Help = "Payload format: aesgcm or victron."
                },
                new ArgSpec
                {
                    Key = "payload",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "value",
                    DefaultValue = "manufacturer",
                    Help = "Payload source: manufacturer, service, or raw."
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
                    Help = "Metric to surface in connect logs (e.g., pv_power, battery_voltage)."
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
        }

        public override async Task<ResultObj> RunCommand(
            string arguments,
            CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            if (!_cmdProcessorStates.IsCmdAvailable)
            {
                var msg = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent.";
                return new ResultObj { Success = false, Message = msg };
            }

            var parsed = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
            if (!parsed.Success)
            {
                var err = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parsed, _schema);
                return new ResultObj { Success = false, Message = err };
            }

            string address = parsed.GetString("address");
            string keyRaw = parsed.GetString("key");
            string format = parsed.GetString("format", "aesgcm");
            string payloadMode = parsed.GetString("payload", "manufacturer");
            int manufacturerId = parsed.GetInt("manufacturer_id", -1);
            string serviceUuid = parsed.GetString("service_uuid");
            string rawPayload = parsed.GetString("raw_payload");

            if (!TryNormalizeAddress(address, out var normalizedAddress, out var addressError))
            {
                return new ResultObj { Success = false, Message = addressError };
            }

            if (!TryParseKey(keyRaw, out var keyBytes, out var keyError))
            {
                return new ResultObj { Success = false, Message = keyError };
            }

#if ANDROID
            if (!OperatingSystem.IsAndroid())
            {
                return new ResultObj { Success = false, Message = "BLE broadcast processor is only available on Android." };
            }

            try
            {
                format = format.Trim().ToLowerInvariant();
                if (format == "victron" && manufacturerId == -1)
                {
                    manufacturerId = 0x02E1; // Victron manufacturer ID
                }

                BleCapture capture;
                if (!string.IsNullOrWhiteSpace(rawPayload))
                {
                    if (!TryParseHex(rawPayload, out var rawBytes, out var rawError))
                    {
                        return new ResultObj { Success = false, Message = rawError };
                    }

                    capture = new BleCapture(normalizedAddress, "raw_input", rawBytes);
                }
                else
                {
                    capture = await ScanOnceAsync(
                        normalizedAddress,
                        payloadMode,
                        manufacturerId,
                        serviceUuid,
                        cancellationToken);
                }

                if (format == "victron")
                {
                    if (!TryDecodeVictron(capture, keyBytes, out var victronMessage, out var victronError))
                    {
                        return new ResultObj { Success = false, Message = victronError };
                    }

                    return new ResultObj { Success = true, Message = victronMessage };
                }

                if (!TryDecryptPayload(capture.Payload, keyBytes, out var plaintext, out var decryptError))
                {
                    var message = BuildOutputMessage(capture, null, decryptError);
                    return new ResultObj { Success = false, Message = message };
                }

                var successMessage = BuildOutputMessage(capture, plaintext, null);
                return new ResultObj { Success = true, Message = successMessage };
            }
            catch (System.OperationCanceledException)
            {
                return new ResultObj { Success = false, Message = "BLE scan canceled or timed out." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BLE scan failed");
                return new ResultObj { Success = false, Message = $"BLE scan failed: {ex.Message}" };
            }
#else
            await Task.CompletedTask;
            return new ResultObj { Success = false, Message = "BLE broadcast processor is only available on Android builds." };
#endif
        }

        public override string GetCommandHelp()
        {
            return CliArgParser.BuildUsage(_cmdProcessorStates.CmdDisplayName, _schema);
        }

#if ANDROID
        private async Task<BleCapture> ScanOnceAsync(
            string address,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            CancellationToken cancellationToken)
        {
            var context = Android.App.Application.Context;
            var manager = (BluetoothManager?)context.GetSystemService(Context.BluetoothService);
            if (manager == null)
            {
                throw new InvalidOperationException("BluetoothManager not available.");
            }

            var adapter = manager.Adapter;
            if (adapter == null || !adapter.IsEnabled)
            {
                throw new InvalidOperationException("Bluetooth adapter is disabled or missing.");
            }

            var scanner = adapter.BluetoothLeScanner;
            if (scanner == null)
            {
                throw new InvalidOperationException("Bluetooth LE scanner not available.");
            }

            var tcs = new TaskCompletionSource<BleCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new BleScanCallback(
                address,
                payloadMode,
                manufacturerId,
                serviceUuid,
                tcs,
                _logger);

            var settings = new ScanSettings.Builder()
                .SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)
                .Build();

            var filters = BuildFilters(address, serviceUuid);
            scanner.StartScan(filters, settings, callback);

            try
            {
                using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                scanner.StopScan(callback);
            }
        }

        private static IList<ScanFilter> BuildFilters(string address, string serviceUuid)
        {
            var filters = new List<ScanFilter>();
            var builder = new ScanFilter.Builder();
            bool hasFilter = false;

            if (!string.IsNullOrWhiteSpace(address))
            {
                builder.SetDeviceAddress(address);
                hasFilter = true;
            }

            if (!string.IsNullOrWhiteSpace(serviceUuid))
            {
                var uuid = UUID.FromString(serviceUuid);
                builder.SetServiceUuid(new ParcelUuid(uuid));
                hasFilter = true;
            }

            if (hasFilter)
            {
                filters.Add(builder.Build());
            }

            return filters;
        }

        private sealed class BleScanCallback : ScanCallback
        {
            private readonly string _targetAddress;
            private readonly string _payloadMode;
            private readonly int _manufacturerId;
            private readonly string _serviceUuid;
            private readonly TaskCompletionSource<BleCapture> _tcs;
            private readonly ILogger _logger;

            public BleScanCallback(
                string targetAddress,
                string payloadMode,
                int manufacturerId,
                string serviceUuid,
                TaskCompletionSource<BleCapture> tcs,
                ILogger logger)
            {
                _targetAddress = targetAddress ?? "";
                _payloadMode = (payloadMode ?? "manufacturer").Trim().ToLowerInvariant();
                _manufacturerId = manufacturerId;
                _serviceUuid = serviceUuid ?? "";
                _tcs = tcs;
                _logger = logger;
            }

            public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
            {
                if (result?.Device == null || result.ScanRecord == null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_targetAddress) &&
                    !string.Equals(result.Device.Address, _targetAddress, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var payload = ExtractPayload(result.ScanRecord, _payloadMode, _manufacturerId, _serviceUuid, out var payloadType);
                if (payload.Length == 0)
                {
                    _logger.LogDebug("BLE scan record had no usable payload.");
                    return;
                }

                _tcs.TrySetResult(new BleCapture(result.Device.Address ?? _targetAddress, payloadType, payload));
            }

            public override void OnScanFailed(ScanFailure errorCode)
            {
                _tcs.TrySetException(new InvalidOperationException($"BLE scan failed: {errorCode}"));
            }
        }

        private static byte[] ExtractPayload(
            ScanRecord record,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            out string payloadType)
        {
            payloadType = payloadMode;

            byte[] payload = payloadMode switch
            {
                "raw" => record.GetBytes() ?? Array.Empty<byte>(),
                "service" => ExtractServiceData(record, serviceUuid),
                _ => ExtractManufacturerData(record, manufacturerId)
            };

            if (payload.Length == 0 && payloadMode != "raw")
            {
                payload = record.GetBytes() ?? Array.Empty<byte>();
                payloadType = "raw";
            }

            return payload;
        }

        private static byte[] ExtractManufacturerData(ScanRecord record, int manufacturerId)
        {
            var raw = record.GetBytes();
            if (raw == null || raw.Length == 0)
            {
                return Array.Empty<byte>();
            }

            return ExtractManufacturerDataFromRaw(raw, manufacturerId);
        }

        private static byte[] ExtractManufacturerDataFromRaw(byte[] raw, int manufacturerId)
        {
            int index = 0;
            while (index < raw.Length)
            {
                int length = raw[index];
                if (length == 0)
                {
                    break;
                }

                int typeIndex = index + 1;
                if (typeIndex >= raw.Length)
                {
                    break;
                }

                byte type = raw[typeIndex];
                int dataIndex = typeIndex + 1;
                int dataLength = length - 1;

                if (dataIndex + dataLength > raw.Length)
                {
                    break;
                }

                if (type == 0xFF && dataLength > 0)
                {
                    var data = new byte[dataLength];
                    Buffer.BlockCopy(raw, dataIndex, data, 0, dataLength);

                    if (manufacturerId >= 0 && dataLength >= 2)
                    {
                        int id = data[0] | (data[1] << 8);
                        if (id != manufacturerId)
                        {
                            index += length + 1;
                            continue;
                        }
                    }

                    return data;
                }

                index += length + 1;
            }

            return Array.Empty<byte>();
        }

        private static byte[] ExtractServiceData(ScanRecord record, string serviceUuid)
        {
            var serviceData = record.ServiceData;
            if (serviceData == null || serviceData.Count == 0)
            {
                return Array.Empty<byte>();
            }

            if (!string.IsNullOrWhiteSpace(serviceUuid))
            {
                foreach (var kvp in serviceData)
                {
                    if (string.Equals(kvp.Key?.ToString(), serviceUuid, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Value ?? Array.Empty<byte>();
                    }
                }
            }

            foreach (var kvp in serviceData)
            {
                return kvp.Value ?? Array.Empty<byte>();
            }

            return Array.Empty<byte>();
        }
#endif

        private static bool TryParseKey(string input, out byte[] keyBytes, out string error)
        {
            keyBytes = Array.Empty<byte>();
            error = "";

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Missing encryption key.";
                return false;
            }

            string trimmed = input.Trim();
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
            }

            if (IsHexString(trimmed))
            {
                try
                {
                    keyBytes = Convert.FromHexString(trimmed);
                }
                catch (Exception ex)
                {
                    error = $"Invalid hex key: {ex.Message}";
                    return false;
                }
            }
            else
            {
                try
                {
                    keyBytes = Convert.FromBase64String(trimmed);
                }
                catch
                {
                    keyBytes = Encoding.UTF8.GetBytes(trimmed);
                }
            }

            if (keyBytes.Length != 16 && keyBytes.Length != 24 && keyBytes.Length != 32)
            {
                error = "Key length must be 16, 24, or 32 bytes (AES-128/192/256).";
                return false;
            }

            return true;
        }

        private static bool TryDecryptPayload(byte[] payload, byte[] key, out byte[] plaintext, out string error)
        {
            plaintext = Array.Empty<byte>();
            error = "";

            if (payload.Length < 12 + 16 + 1)
            {
                error = "Payload is too short for AES-GCM (need nonce + tag + data).";
                return false;
            }

            try
            {
                ReadOnlySpan<byte> nonce = payload.AsSpan(0, 12);
                ReadOnlySpan<byte> tag = payload.AsSpan(payload.Length - 16, 16);
                ReadOnlySpan<byte> ciphertext = payload.AsSpan(12, payload.Length - 12 - 16);

                plaintext = new byte[ciphertext.Length];
                using var aes = new AesGcm(key);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return true;
            }
            catch (CryptographicException ex)
            {
                error = $"Decryption failed: {ex.Message}";
                return false;
            }
        }

        private static bool TryNormalizeAddress(string input, out string normalized, out string error)
        {
            normalized = "";
            error = "";

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Missing BLE address.";
                return false;
            }

            var trimmed = input.Trim();
            if (trimmed.Length == 12 && IsHexString(trimmed))
            {
                normalized = string.Create(17, trimmed, (span, hex) =>
                {
                    int di = 0;
                    for (int i = 0; i < hex.Length; i += 2)
                    {
                        if (di > 0) span[di++] = ':';
                        span[di++] = char.ToUpperInvariant(hex[i]);
                        span[di++] = char.ToUpperInvariant(hex[i + 1]);
                    }
                });
                return true;
            }

            if (IsColonMac(trimmed))
            {
                normalized = trimmed.ToUpperInvariant();
                return true;
            }

            if (TryNormalizeDashedMac(trimmed, out var dashed))
            {
                normalized = dashed;
                return true;
            }

            error = $"Invalid BLE address format: {input}. Expected 12 hex chars or AA:BB:CC:DD:EE:FF.";
            return false;
        }

        private static bool IsColonMac(string value)
        {
            if (value.Length != 17) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if ((i + 1) % 3 == 0)
                {
                    if (value[i] != ':') return false;
                }
                else
                {
                    char c = value[i];
                    bool isHex = (c >= '0' && c <= '9')
                                 || (c >= 'a' && c <= 'f')
                                 || (c >= 'A' && c <= 'F');
                    if (!isHex) return false;
                }
            }
            return true;
        }

        private static bool TryNormalizeDashedMac(string value, out string normalized)
        {
            normalized = "";
            if (value.Length != 17) return false;

            for (int i = 0; i < value.Length; i++)
            {
                if ((i + 1) % 3 == 0)
                {
                    if (value[i] != '-') return false;
                }
                else
                {
                    char c = value[i];
                    bool isHex = (c >= '0' && c <= '9')
                                 || (c >= 'a' && c <= 'f')
                                 || (c >= 'A' && c <= 'F');
                    if (!isHex) return false;
                }
            }

            normalized = value.Replace('-', ':').ToUpperInvariant();
            return true;
        }

        private static bool IsHexString(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length % 2 != 0)
            {
                return false;
            }

            foreach (char c in value)
            {
                bool isHex = (c >= '0' && c <= '9')
                             || (c >= 'a' && c <= 'f')
                             || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
            }

            return true;
        }

        private static string BuildOutputMessage(BleCapture capture, byte[]? plaintext, string? error)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"BLE address: {capture.Address}");
            sb.AppendLine($"Payload ({capture.PayloadType}): {ToHex(capture.Payload)}");

            if (plaintext != null)
            {
                sb.AppendLine($"Decrypted: {FormatPlaintext(plaintext)}");
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                sb.AppendLine($"Error: {error}");
            }

            return sb.ToString().Trim();
        }

        private static bool TryDecodeVictron(BleCapture capture, byte[] keyBytes, out string message, out string error)
        {
            message = "";
            error = "";

            if (keyBytes.Length != 16)
            {
                error = "Victron decode requires a 16-byte AES-128 key.";
                return false;
            }

            if (!TryExtractVictronRecord(capture.Payload, out var record, out var extractError))
            {
                error = extractError;
                return false;
            }

            if (record.KeyCheck != keyBytes[0])
            {
                error = $"Victron key check mismatch (header=0x{record.KeyCheck:X2}, key[0]=0x{keyBytes[0]:X2}).";
                return false;
            }

            if (record.Cipher.Length == 0 || record.Cipher.Length > 16)
            {
                error = $"Victron cipher length {record.Cipher.Length} is invalid (expected 1..16).";
                return false;
            }

            byte[] plaintext = DecryptVictronAesCtr(keyBytes, record.Nonce, record.Cipher);

            var sb = new StringBuilder();
            sb.AppendLine($"BLE address: {capture.Address}");
            sb.AppendLine($"Payload ({capture.PayloadType}): {ToHex(capture.Payload)}");
            sb.AppendLine($"Victron recordType: 0x{record.RecordType:X2}");
            sb.AppendLine($"Victron nonce: 0x{record.Nonce:X4}");
            sb.AppendLine($"Victron plaintext: {ToHex(plaintext)}");

            if (record.RecordType == 0x01)
            {
                if (plaintext.Length < 10)
                {
                    error = $"Victron solar payload too short ({plaintext.Length}).";
                    return false;
                }

                byte deviceState = plaintext[0];
                byte chargerError = plaintext[1];
                short batteryVoltageRaw = BinaryPrimitives.ReadInt16LittleEndian(plaintext.AsSpan(2, 2));
                short batteryCurrentRaw = BinaryPrimitives.ReadInt16LittleEndian(plaintext.AsSpan(4, 2));
                ushort yieldTodayRaw = BinaryPrimitives.ReadUInt16LittleEndian(plaintext.AsSpan(6, 2));
                ushort pvPowerRaw = BinaryPrimitives.ReadUInt16LittleEndian(plaintext.AsSpan(8, 2));

                double batteryVoltage = batteryVoltageRaw / 100.0;
                double batteryCurrent = batteryCurrentRaw / 10.0;
                double yieldToday = yieldTodayRaw / 100.0;

                sb.AppendLine($"Battery voltage: {batteryVoltage:F2} V");
                sb.AppendLine($"Battery current: {batteryCurrent:F1} A");
                sb.AppendLine($"Yield today: {yieldToday:F2} kWh");
                sb.AppendLine($"PV power: {pvPowerRaw} W");
                sb.AppendLine($"Device state: {deviceState}");
                sb.AppendLine($"Charger error: {chargerError}");

                if (plaintext.Length >= 12)
                {
                    ushort load9 = (ushort)(plaintext[10] | ((plaintext[11] & 0x01) << 8));
                    double? loadCurrentA = load9 == 0x1FF ? null : load9 / 10.0;
                    sb.AppendLine(loadCurrentA is null
                        ? "Load current: NA"
                        : $"Load current: {loadCurrentA:F1} A");
                }
            }

            message = sb.ToString().Trim();
            return true;
        }

        private struct VictronRecord
        {
            public byte RecordType;
            public ushort Nonce;
            public byte KeyCheck;
            public byte[] Cipher;
        }

        private static bool TryExtractVictronRecord(byte[] payload, out VictronRecord record, out string error)
        {
            record = default;
            error = "";

            if (payload.Length < 6)
            {
                error = "Victron payload too short.";
                return false;
            }

            var span = payload.AsSpan();
            if (span.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(span) == 0x02E1)
            {
                span = span.Slice(2);
            }

            if (span.Length < 6)
            {
                error = "Victron payload too short after company ID.";
                return false;
            }

            int offset = 0;
            if (span[0] == 0x10)
            {
                // Product advertisement record; extra record starts at index 4.
                if (span.Length < 9)
                {
                    error = "Victron product advertisement too short.";
                    return false;
                }
                offset = 4;
            }

            if (span.Length < offset + 4)
            {
                error = "Victron extra record header missing.";
                return false;
            }

            record.RecordType = span[offset];
            record.Nonce = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset + 1, 2));
            record.KeyCheck = span[offset + 3];
            record.Cipher = span.Slice(offset + 4).ToArray();

            if (span.Length > 24)
            {
                error = "Victron payload too long.";
                return false;
            }

            return true;
        }

        private static byte[] DecryptVictronAesCtr(byte[] key, ushort nonce, ReadOnlySpan<byte> cipher)
        {
            byte[] counterBlock = new byte[16];
            counterBlock[0] = (byte)(nonce & 0xFF);
            counterBlock[1] = (byte)(nonce >> 8);

            byte[] keystream = new byte[16];
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                using var enc = aes.CreateEncryptor();
                enc.TransformBlock(counterBlock, 0, 16, keystream, 0);
            }

            byte[] plain = new byte[cipher.Length];
            for (int i = 0; i < cipher.Length; i++)
            {
                plain[i] = (byte)(cipher[i] ^ keystream[i]);
            }

            return plain;
        }

        private static string ToHex(byte[] data)
        {
            if (data.Length == 0) return "";
            return Convert.ToHexString(data);
        }

        private static string FormatPlaintext(byte[] data)
        {
            if (data.Length == 0) return "";
            string text;
            try
            {
                text = Encoding.UTF8.GetString(data);
            }
            catch
            {
                return ToHex(data);
            }

            int printable = 0;
            foreach (char c in text)
            {
                if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
                {
                    printable++;
                }
            }

            return printable >= text.Length * 0.7 ? text : ToHex(data);
        }

        private static bool TryParseHex(string value, out byte[] bytes, out string error)
        {
            bytes = Array.Empty<byte>();
            error = "";
            var trimmed = value?.Trim() ?? "";
            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(2);
            }

            if (!IsHexString(trimmed))
            {
                error = "Raw payload must be hex (even length).";
                return false;
            }

            try
            {
                bytes = Convert.FromHexString(trimmed);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Invalid raw payload hex: {ex.Message}";
                return false;
            }
        }
    }
}
