using System;
using System.Collections.Generic;
using System.Linq;
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

namespace NetworkMonitor.Connection
{
    public class BleBroadcastCmdProcessor : CmdProcessor, IBleBroadcastSnapshotProcessor
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
        }

        public IBleAdvertisementListener? Listener { get; set; }

        public override Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null) => Task.FromResult(
                ReadSnapshot(arguments, Listener?.Snapshot, TimeSpan.FromMilliseconds(BleBroadcastConnect.DefaultTimeoutMilliseconds * 10L), cancellationToken));

        public ResultObj ReadSnapshot(string arguments, BleAdvertisementSnapshot? snapshot,
            TimeSpan window, CancellationToken cancellationToken)
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
            int nonceLength = parsed.GetInt("nonce_len", 12);
            int tagLength = parsed.GetInt("tag_len", 16);
            string nonceAt = parsed.GetString("nonce_at", "start");
            var selectedDecoder = BlePayloadDecoderRegistry.Default.Find(format);
            string payloadMode = parsed.GetString("payload", selectedDecoder?.DefaultPayloadMode ?? "manufacturer");
            int manufacturerId = parsed.GetInt("manufacturer_id", -1);
            if (manufacturerId < 0 && selectedDecoder?.ManufacturerId is int protocolManufacturer)
                manufacturerId = protocolManufacturer;
            string serviceUuid = parsed.GetString("service_uuid", selectedDecoder?.ServiceUuid ?? "");
            string rawPayload = parsed.GetString("raw_payload");

            if (!TryNormalizeAddress(address, out var normalizedAddress, out var addressError))
            {
                return new ResultObj { Success = false, Message = addressError };
            }

            if (!BleKeyParser.TryParse(keyRaw, selectedDecoder, out var keyBytes, out var keyError))
            {
                return new ResultObj { Success = false, Message = keyError };
            }

            if (!TryParseNoncePlacement(nonceAt, out var noncePlacement))
            {
                return new ResultObj { Success = false, Message = "nonce_at must be start or end." };
            }


            cancellationToken.ThrowIfCancellationRequested();
            var options = new BleCryptoOptions(nonceLength, tagLength, noncePlacement);
            if (!string.IsNullOrWhiteSpace(rawPayload))
            {
                if (!TryParseHex(rawPayload, out var bytes, out var error))
                    return new ResultObj { Success = false, Message = error };
                return BuildResult(format, new BleCapture(normalizedAddress, "raw_input", bytes), keyBytes, options);
            }
#if !ANDROID && !WINDOWS
            if (snapshot == null) return new ResultObj { Success = false,
                Message = "BLE scanning is only available on Android or Windows builds; Linux requires BlueZ/D-Bus integration." };
#endif
            if (snapshot == null || snapshot.LastSequence == 0)
                return new ResultObj { Success = false, Message = string.IsNullOrEmpty(snapshot?.Error)
                    ? "No BLE advertisements in the completed processor cycle." : snapshot.Error };
            if (!string.IsNullOrEmpty(snapshot.Error))
                return new ResultObj { Success = false, Message = snapshot.Error };

            string metric = BleMetricSelector.Canonical(parsed.GetString("metric", "pv_power"));
            var samples = new List<double>();
            ResultObj? latest = null;
            ResultObj? latestError = null;
            ResultObj? latestDecoded = null;
            foreach (var packet in snapshot.Window(normalizedAddress, window))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var payload = BleAdvertisementPayload.Extract(packet.CopyBytes(), payloadMode,
                    manufacturerId, serviceUuid, out var payloadType);
                if (payload.Length == 0 || (selectedDecoder != null &&
                    !selectedDecoder.Accepts(payload, payloadType, keyBytes.Length > 0 ? keyBytes[0] : (byte)0))) continue;
                var decoded = BuildResult(format, new BleCapture(normalizedAddress, payloadType, payload), keyBytes, options);
                if (!decoded.Success) { latestError = decoded; continue; }
                latestDecoded = decoded;
                if (decoded.Data is BleDecodedPayload data)
                {
                    var matches = data.Readings.Where(r => r.Metric == metric).ToArray();
                    if (matches.Length != 1 || !matches[0].Value.HasValue || !double.IsFinite(matches[0].Value!.Value))
                    {
                        latestError = new ResultObj { Success = false,
                            Message = $"BLE metric '{metric}' is missing, unavailable or ambiguous.\n{decoded.Message}" };
                        continue;
                    }
                    samples.Add(matches[0].Value!.Value);
                }
                latest = decoded;
            }
            if (latest == null) return latestError ?? new ResultObj { Success = false,
                Message = "No usable BLE advertisements in the configured measurement window." };
            if (latest.Data is BleDecodedPayload last && samples.Count > 0)
            {
                // Average physical values; the connect performs unsigned scale/offset encoding once.
                latest.Message = latestDecoded!.Message;
                latest.Data = last with { Readings = last.Readings.Select(r => r.Metric == metric
                    ? r with { Value = samples.Average() } : r).ToArray() };
            }
            return latest;
        }

        public override string GetCommandHelp()
        {
            return CliArgParser.BuildUsage(_cmdProcessorStates.CmdDisplayName, _schema);
        }

        private ResultObj BuildResult(string format, BleCapture capture, byte[] keyBytes, BleCryptoOptions cryptoOptions)
        {
            format = BlePayloadDecoderRegistry.Default.Find(format)?.Format
                ?? BleCryptoHelper.NormalizeFormat(format, keyBytes.Length > 0);

            if (BlePayloadDecoderRegistry.Default.Find(format) is { } decoder)
            {
                if (!decoder.TryDecodeReadings(new BlePayload(capture.Address, capture.PayloadType, capture.Payload), keyBytes, out var decoded, out var decodeError))
                {
                    var message = BuildOutputMessage(capture, null, decodeError);
                    return new ResultObj { Success = false, Message = message };
                }

                return new ResultObj { Success = true, Message = decoded.Message + "\n" + decoded.MetricSummary, Data = decoded };
            }

            if (!BleCryptoHelper.TryDecryptPayload(format, capture.Payload, keyBytes, cryptoOptions, out var plaintext, out var decryptError))
            {
                var message = BuildOutputMessage(capture, capture.Payload, $"Decryption failed; showing raw payload. {decryptError}");
                return new ResultObj { Success = true, Message = message };
            }

            var successMessage = BuildOutputMessage(capture, plaintext, null);
            return new ResultObj { Success = true, Message = successMessage };
        }

        private static bool TryParseKey(string input, out byte[] keyBytes, out string error) =>
            BleKeyParser.TryParse(input, null, out keyBytes, out error);

        private static bool TryParseNoncePlacement(string value, out BleNoncePlacement placement)
        {
            placement = BleNoncePlacement.Start;
            var normalized = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || normalized == "start")
            {
                placement = BleNoncePlacement.Start;
                return true;
            }
            if (normalized == "end")
            {
                placement = BleNoncePlacement.End;
                return true;
            }

            return false;
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
