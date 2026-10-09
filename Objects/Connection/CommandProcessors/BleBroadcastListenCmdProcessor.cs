using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;

namespace NetworkMonitor.Connection
{
    public class BleBroadcastListenCmdProcessor : CmdProcessor, IBleListenSnapshotProcessor
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

        public BleBroadcastListenCmdProcessor(
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

        public IBleAdvertisementListener? Listener { get; set; }

        public override Task<ResultObj> RunCommand(string arguments, CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null) => Task.FromResult(
                ReadSnapshot(arguments, Listener?.Snapshot, 0, cancellationToken));

        public ResultObj ReadSnapshot(string arguments, BleAdvertisementSnapshot? snapshot,
            long afterSequence, CancellationToken cancellationToken)
        {
            if (!_cmdProcessorStates.IsCmdAvailable)
                return new ResultObj { Success = false, Message = $"{_cmdProcessorStates.CmdDisplayName} is not available on this agent." };
            var parsed = CliArgParser.Parse(arguments, _schema, allowUnknown: false, fillDefaults: true);
            if (!parsed.Success) return new ResultObj { Success = false,
                Message = CliArgParser.BuildErrorMessage(_cmdProcessorStates.CmdDisplayName, parsed, _schema) };
            cancellationToken.ThrowIfCancellationRequested();
            string rawPayload = parsed.GetString("raw_payload");
            var captures = new List<BleCapture>();
            if (!string.IsNullOrWhiteSpace(rawPayload))
            {
                if (!TryParseHex(rawPayload, out var bytes, out var error))
                    return new ResultObj { Success = false, Message = error };
                captures.Add(new BleCapture("raw_input", "raw_input", bytes));
            }
            else
            {
#if !ANDROID && !WINDOWS
                if (snapshot == null) return new ResultObj { Success = false,
                    Message = "BLE scanning is only available on Android or Windows builds; Linux requires BlueZ/D-Bus integration." };
#endif
                if (snapshot == null) return new ResultObj { Success = true, Message = "BLE listen captured 0 advertisement(s)." };
                if (!string.IsNullOrEmpty(snapshot.Error)) return new ResultObj { Success = false, Message = snapshot.Error };
                string mode = parsed.GetString("payload", "raw");
                int manufacturer = parsed.GetInt("manufacturer_id", -1);
                string service = parsed.GetString("service_uuid");
                if (mode == "raw" && manufacturer >= 0) mode = "manufacturer";
                if (mode == "raw" && !string.IsNullOrWhiteSpace(service)) mode = "service";
                foreach (var packet in snapshot.Packets.Where(p => p.Sequence > afterSequence))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bytes = BleAdvertisementPayload.Extract(packet.CopyBytes(), mode, manufacturer, service, out var type);
                    if (bytes.Length > 0) captures.Add(new BleCapture(FormatAddress(packet.Address), type, bytes));
                }
            }
            var text = new StringBuilder($"BLE listen captured {captures.Count} advertisement(s).\n");
            for (int i = 0; i < captures.Count; i++)
            {
                text.AppendLine($"\n--- Capture {i + 1} ---");
                text.AppendLine(BuildOutputMessage(captures[i], null, null));
            }
            return new ResultObj { Success = true, Message = text.ToString().Trim() };
        }
        private static string FormatAddress(string address) => address.Length == 12
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => address.Substring(i * 2, 2))) : address;

        public override string GetCommandHelp()
        {
            return CliArgParser.BuildUsage(_cmdProcessorStates.CmdDisplayName, _schema);
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
