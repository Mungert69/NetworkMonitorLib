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
#if WINDOWS
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;
#endif

namespace NetworkMonitor.Connection
{
    public class BleBroadcastListenCmdProcessor : CmdProcessor
    {
        private readonly List<ArgSpec> _schema;
        private const int DefaultMaxCaptures = 10;

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

        private sealed class BleListenScanResult
        {
            public BleListenScanResult(List<BleCapture> captures, string endReason)
            {
                Captures = captures;
                EndReason = endReason;
            }

            public List<BleCapture> Captures { get; }
            public string EndReason { get; }
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
                    Help = "Encryption key (hex, base64, or raw; 16/24/32 bytes). Optional."
                },
                new ArgSpec
                {
                    Key = "format",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "value",
                    DefaultValue = "aesgcm",
                    Help = "Payload format: raw, aesgcm, aesctr, or victron."
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
                    Key = "max_captures",
                    Required = false,
                    IsFlag = false,
                    TypeHint = "int",
                    DefaultValue = "10",
                    Help = "Maximum number of BLE captures before stopping (default 10)."
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

            string keyRaw = parsed.GetString("key");
            string format = parsed.GetString("format", "aesgcm");
            int nonceLength = parsed.GetInt("nonce_len", 12);
            int tagLength = parsed.GetInt("tag_len", 16);
            string nonceAt = parsed.GetString("nonce_at", "start");
            string payloadMode = parsed.GetString("payload", "manufacturer");
            int manufacturerId = parsed.GetInt("manufacturer_id", -1);
            string serviceUuid = parsed.GetString("service_uuid");
            int maxCaptures = parsed.GetInt("max_captures", DefaultMaxCaptures);
            string rawPayload = parsed.GetString("raw_payload");

            string normalizedAddress = "";

            if (!TryParseKey(keyRaw, out var keyBytes, out var keyError))
            {
                return new ResultObj { Success = false, Message = keyError };
            }

            if (maxCaptures <= 0)
            {
                return new ResultObj { Success = false, Message = "max_captures must be greater than zero." };
            }

            if (!TryParseNoncePlacement(nonceAt, out var noncePlacement))
            {
                return new ResultObj { Success = false, Message = "nonce_at must be start or end." };
            }

#if ANDROID
            return await RunAndroidAsync(
                normalizedAddress,
                keyBytes,
                format,
                new BleCryptoOptions(nonceLength, tagLength, noncePlacement),
                payloadMode,
                manufacturerId,
                serviceUuid,
                maxCaptures,
                rawPayload,
                cancellationToken);
#elif WINDOWS
            return await RunWindowsAsync(
                normalizedAddress,
                keyBytes,
                format,
                new BleCryptoOptions(nonceLength, tagLength, noncePlacement),
                payloadMode,
                manufacturerId,
                serviceUuid,
                maxCaptures,
                rawPayload,
                cancellationToken);
#else
            if (OperatingSystem.IsLinux())
            {
                return await RunLinuxAsync(
                    normalizedAddress,
                    keyBytes,
                    format,
                    new BleCryptoOptions(nonceLength, tagLength, noncePlacement),
                    payloadMode,
                    manufacturerId,
                    serviceUuid,
                    maxCaptures,
                    rawPayload,
                    cancellationToken);
            }

            await Task.CompletedTask;
            return new ResultObj { Success = false, Message = "BLE broadcast listen processor is only available on Android or Windows builds." };
#endif
        }

        public override string GetCommandHelp()
        {
            return CliArgParser.BuildUsage(_cmdProcessorStates.CmdDisplayName, _schema);
        }

#if ANDROID
        private async Task<ResultObj> RunAndroidAsync(
            string normalizedAddress,
            byte[] keyBytes,
            string format,
            BleCryptoOptions cryptoOptions,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            int maxCaptures,
            string rawPayload,
            CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsAndroid())
            {
                return new ResultObj { Success = false, Message = "BLE broadcast listen processor is only available on Android or Windows." };
            }

            try
            {
                format = format.Trim().ToLowerInvariant();
                if (format == "victron" && manufacturerId == -1)
                {
                    manufacturerId = 0x02E1; // Victron manufacturer ID
                }

                BleListenScanResult scanResult;
                if (!string.IsNullOrWhiteSpace(rawPayload))
                {
                    if (!TryParseHex(rawPayload, out var rawBytes, out var rawError))
                    {
                        return new ResultObj { Success = false, Message = rawError };
                    }

                    var captureAddress = string.IsNullOrWhiteSpace(normalizedAddress) ? "unknown" : normalizedAddress;
                    scanResult = new BleListenScanResult(
                        new List<BleCapture> { new BleCapture(captureAddress, "raw_input", rawBytes) },
                        "raw_payload");
                }
                else
                {
                    scanResult = await ScanListenAsync(
                        normalizedAddress,
                        payloadMode,
                        manufacturerId,
                        serviceUuid,
                        maxCaptures,
                        cancellationToken,
                        format == "victron" && keyBytes.Length > 0,
                        keyBytes.Length > 0 ? keyBytes[0] : (byte)0);
                }

                return BuildListenResult(format, scanResult, keyBytes, cryptoOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BLE scan failed");
                return new ResultObj { Success = false, Message = $"BLE scan failed: {ex.Message}" };
            }
        }

        private async Task<BleListenScanResult> ScanListenAsync(
            string address,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            int maxCaptures,
            CancellationToken cancellationToken,
            bool requireVictronInstantReadout,
            byte victronKeyFirstByte)
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

            var captures = new List<BleCapture>();
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new BleListenScanCallback(
                address,
                payloadMode,
                manufacturerId,
                serviceUuid,
                requireVictronInstantReadout,
                victronKeyFirstByte,
                maxCaptures,
                captures,
                tcs,
                _logger);

            var settings = new ScanSettings.Builder()
                .SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)
                .Build();

            var filters = BuildFilters(address, serviceUuid);
            scanner.StartScan(filters, settings, callback);

            string endReason = "timeout";
            try
            {
                using (cancellationToken.Register(() => tcs.TrySetResult("timeout")))
                {
                    endReason = await tcs.Task;
                }
            }
            finally
            {
                scanner.StopScan(callback);
            }

            return new BleListenScanResult(captures, endReason);
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

        private sealed class BleListenScanCallback : ScanCallback
        {
            private readonly string _targetAddress;
            private readonly string _payloadMode;
            private readonly int _manufacturerId;
            private readonly string _serviceUuid;
            private readonly bool _requireVictronInstantReadout;
            private readonly byte _victronKeyFirstByte;
            private readonly int _maxCaptures;
            private readonly List<BleCapture> _captures;
            private readonly object _lock = new object();
            private readonly TaskCompletionSource<string> _tcs;
            private readonly ILogger _logger;

            public BleListenScanCallback(
                string targetAddress,
                string payloadMode,
                int manufacturerId,
                string serviceUuid,
                bool requireVictronInstantReadout,
                byte victronKeyFirstByte,
                int maxCaptures,
                List<BleCapture> captures,
                TaskCompletionSource<string> tcs,
                ILogger logger)
            {
                _targetAddress = targetAddress ?? "";
                _payloadMode = (payloadMode ?? "manufacturer").Trim().ToLowerInvariant();
                _manufacturerId = manufacturerId;
                _serviceUuid = serviceUuid ?? "";
                _requireVictronInstantReadout = requireVictronInstantReadout;
                _victronKeyFirstByte = victronKeyFirstByte;
                _maxCaptures = maxCaptures;
                _captures = captures;
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

                if (_requireVictronInstantReadout && !IsVictronInstantReadout(payload, payloadType, _victronKeyFirstByte))
                {
                    _logger.LogDebug("Ignoring non-Victron instant readout packet. {Details}", DescribeVictronPayload(payload, payloadType));
                    return;
                }

                bool shouldComplete = false;
                lock (_lock)
                {
                    if (_captures.Count < _maxCaptures)
                    {
                        _captures.Add(new BleCapture(result.Device.Address ?? _targetAddress, payloadType, payload));
                        if (_captures.Count >= _maxCaptures)
                        {
                            shouldComplete = true;
                        }
                    }
                }

                if (shouldComplete)
                {
                    _tcs.TrySetResult("capture_limit");
                }
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

        private async Task<ResultObj> RunLinuxAsync(
            string normalizedAddress,
            byte[] keyBytes,
            string format,
            BleCryptoOptions cryptoOptions,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            int maxCaptures,
            string rawPayload,
            CancellationToken cancellationToken)
        {
            try
            {
                format = format.Trim().ToLowerInvariant();
                if (format == "victron" && manufacturerId == -1)
                {
                    manufacturerId = 0x02E1;
                }

                BleListenScanResult scanResult;
                if (!string.IsNullOrWhiteSpace(rawPayload))
                {
                    if (!TryParseHex(rawPayload, out var rawBytes, out var rawError))
                    {
                        return new ResultObj { Success = false, Message = rawError };
                    }

                    var captureAddress = string.IsNullOrWhiteSpace(normalizedAddress) ? "unknown" : normalizedAddress;
                    scanResult = new BleListenScanResult(
                        new List<BleCapture> { new BleCapture(captureAddress, "raw_input", rawBytes) },
                        "raw_payload");
                }
                else
                {
                    scanResult = await ScanListenLinuxAsync(
                        normalizedAddress,
                        payloadMode,
                        manufacturerId,
                        serviceUuid,
                        maxCaptures,
                        cancellationToken,
                        format == "victron" && keyBytes.Length > 0,
                        keyBytes.Length > 0 ? keyBytes[0] : (byte)0);
                }

                return BuildListenResult(format, scanResult, keyBytes, cryptoOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BLE scan failed");
                return new ResultObj { Success = false, Message = $"BLE scan failed: {ex.Message}" };
            }
        }

        private Task<BleListenScanResult> ScanListenLinuxAsync(
            string address,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            int maxCaptures,
            CancellationToken cancellationToken,
            bool requireVictronInstantReadout,
            byte victronKeyFirstByte)
        {
            _ = address;
            _ = payloadMode;
            _ = manufacturerId;
            _ = serviceUuid;
            _ = maxCaptures;
            _ = cancellationToken;
            _ = requireVictronInstantReadout;
            _ = victronKeyFirstByte;

            throw new NotSupportedException(
                "BLE scan on Linux requires BlueZ/D-Bus integration and privileged access to the host BLE adapter.");
        }

#if WINDOWS
        private async Task<ResultObj> RunWindowsAsync(
            string normalizedAddress,
            byte[] keyBytes,
            string format,
            BleCryptoOptions cryptoOptions,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            int maxCaptures,
            string rawPayload,
            CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                return new ResultObj { Success = false, Message = "BLE broadcast listen processor is only available on Android or Windows." };
            }

            try
            {
                format = format.Trim().ToLowerInvariant();
                if (format == "victron" && manufacturerId == -1)
                {
                    manufacturerId = 0x02E1; // Victron manufacturer ID
                }

                BleListenScanResult scanResult;
                if (!string.IsNullOrWhiteSpace(rawPayload))
                {
                    if (!TryParseHex(rawPayload, out var rawBytes, out var rawError))
                    {
                        return new ResultObj { Success = false, Message = rawError };
                    }

                    var captureAddress = string.IsNullOrWhiteSpace(normalizedAddress) ? "unknown" : normalizedAddress;
                    scanResult = new BleListenScanResult(
                        new List<BleCapture> { new BleCapture(captureAddress, "raw_input", rawBytes) },
                        "raw_payload");
                }
                else
                {
                    scanResult = await ScanListenWindowsAsync(
                        normalizedAddress,
                        payloadMode,
                        manufacturerId,
                        serviceUuid,
                        maxCaptures,
                        cancellationToken,
                        format == "victron" && keyBytes.Length > 0,
                        keyBytes.Length > 0 ? keyBytes[0] : (byte)0);
                }

                return BuildListenResult(format, scanResult, keyBytes, cryptoOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BLE scan failed");
                return new ResultObj { Success = false, Message = $"BLE scan failed: {ex.Message}" };
            }
        }

        private async Task<BleListenScanResult> ScanListenWindowsAsync(
            string address,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            int maxCaptures,
            CancellationToken cancellationToken,
            bool requireVictronInstantReadout,
            byte victronKeyFirstByte)
        {
            var captures = new List<BleCapture>();
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            var gate = new object();

            void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
            {
                var mac = FormatBluetoothAddress(args.BluetoothAddress);
                if (!string.IsNullOrWhiteSpace(address) &&
                    !string.Equals(mac, address, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var payload = ExtractWindowsPayload(args.Advertisement, payloadMode, manufacturerId, serviceUuid, out var payloadType);
                if (payload.Length == 0)
                {
                    _logger.LogDebug("BLE scan record had no usable payload.");
                    return;
                }

                if (requireVictronInstantReadout && !IsVictronInstantReadout(payload, payloadType, victronKeyFirstByte))
                {
                    _logger.LogDebug("Ignoring non-Victron instant readout packet. {Details}", DescribeVictronPayload(payload, payloadType));
                    return;
                }

                bool shouldComplete = false;
                lock (gate)
                {
                    if (captures.Count < maxCaptures)
                    {
                        captures.Add(new BleCapture(mac, payloadType, payload));
                        if (captures.Count >= maxCaptures)
                        {
                            shouldComplete = true;
                        }
                    }
                }

                if (shouldComplete)
                {
                    tcs.TrySetResult("capture_limit");
                }
            }

            watcher.Received += OnReceived;
            watcher.Start();

            string endReason = "timeout";
            try
            {
                using (cancellationToken.Register(() => tcs.TrySetResult("timeout")))
                {
                    endReason = await tcs.Task;
                }
            }
            finally
            {
                watcher.Stop();
                watcher.Received -= OnReceived;
            }

            return new BleListenScanResult(captures, endReason);
        }

        private static byte[] ExtractWindowsPayload(
            BluetoothLEAdvertisement advertisement,
            string payloadMode,
            int manufacturerId,
            string serviceUuid,
            out string payloadType)
        {
            payloadType = payloadMode;

            byte[] payload = payloadMode switch
            {
                "raw" => BuildRawAdvertisement(advertisement),
                "service" => ExtractWindowsServiceData(advertisement, serviceUuid),
                _ => ExtractWindowsManufacturerData(advertisement, manufacturerId)
            };

            if (payload.Length == 0 && payloadMode != "raw")
            {
                payload = BuildRawAdvertisement(advertisement);
                payloadType = "raw";
            }

            return payload;
        }

        private static byte[] ExtractWindowsManufacturerData(BluetoothLEAdvertisement advertisement, int manufacturerId)
        {
            foreach (var md in advertisement.ManufacturerData)
            {
                if (manufacturerId >= 0 && md.CompanyId != manufacturerId)
                {
                    continue;
                }

                var data = BufferToBytes(md.Data);
                var result = new byte[data.Length + 2];
                result[0] = (byte)(md.CompanyId & 0xFF);
                result[1] = (byte)((md.CompanyId >> 8) & 0xFF);
                Buffer.BlockCopy(data, 0, result, 2, data.Length);
                return result;
            }

            return Array.Empty<byte>();
        }

        private static byte[] ExtractWindowsServiceData(BluetoothLEAdvertisement advertisement, string serviceUuid)
        {
            var desired = TryNormalizeServiceUuid(serviceUuid, out var normalizedGuid) ? normalizedGuid : (Guid?)null;
            if (desired.HasValue && !advertisement.ServiceUuids.Contains(desired.Value))
            {
                return Array.Empty<byte>();
            }

            foreach (var section in advertisement.DataSections)
            {
                if (section.DataType != 0x16 && section.DataType != 0x20 && section.DataType != 0x21)
                {
                    continue;
                }

                var data = BufferToBytes(section.Data);
                if (data.Length == 0)
                {
                    continue;
                }

                if (desired.HasValue)
                {
                    if (section.DataType == 0x16 && data.Length >= 2)
                    {
                        ushort uuid16 = (ushort)(data[0] | (data[1] << 8));
                        if (desired.Value == BluetoothUuidFrom16Bit(uuid16))
                        {
                            return data.AsSpan(2).ToArray();
                        }
                    }
                    else if (section.DataType == 0x20 && data.Length >= 4)
                    {
                        uint uuid32 = (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
                        if (desired.Value == BluetoothUuidFrom32Bit(uuid32))
                        {
                            return data.AsSpan(4).ToArray();
                        }
                    }
                    else if (section.DataType == 0x21 && data.Length >= 16)
                    {
                        var uuid = new Guid(data.AsSpan(0, 16).ToArray());
                        if (desired.Value == uuid)
                        {
                            return data.AsSpan(16).ToArray();
                        }
                    }
                }

                return data;
            }

            return Array.Empty<byte>();
        }

        private static byte[] BuildRawAdvertisement(BluetoothLEAdvertisement advertisement)
        {
            var bytes = new List<byte>();

            foreach (var md in advertisement.ManufacturerData)
            {
                var data = BufferToBytes(md.Data);
                var payload = new byte[data.Length + 2];
                payload[0] = (byte)(md.CompanyId & 0xFF);
                payload[1] = (byte)((md.CompanyId >> 8) & 0xFF);
                Buffer.BlockCopy(data, 0, payload, 2, data.Length);
                AppendAdStructure(bytes, 0xFF, payload);
            }

            foreach (var section in advertisement.DataSections)
            {
                var data = BufferToBytes(section.Data);
                if (data.Length == 0)
                {
                    continue;
                }
                AppendAdStructure(bytes, section.DataType, data);
            }

            return bytes.ToArray();
        }

        private static void AppendAdStructure(List<byte> bytes, byte type, ReadOnlySpan<byte> data)
        {
            int length = data.Length + 1;
            if (length > 255)
            {
                return;
            }
            bytes.Add((byte)length);
            bytes.Add(type);
            for (int i = 0; i < data.Length; i++)
            {
                bytes.Add(data[i]);
            }
        }

        private static byte[] BufferToBytes(IBuffer buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var bytes = new byte[buffer.Length];
            using var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(bytes);
            return bytes;
        }

        private static string FormatBluetoothAddress(ulong address)
        {
            Span<char> chars = stackalloc char[17];
            int pos = 0;
            for (int i = 5; i >= 0; i--)
            {
                if (pos > 0)
                {
                    chars[pos++] = ':';
                }
                byte b = (byte)(address >> (i * 8));
                chars[pos++] = ToHexChar((b >> 4) & 0xF);
                chars[pos++] = ToHexChar(b & 0xF);
            }
            return new string(chars);
        }

        private static char ToHexChar(int value)
        {
            return (char)(value < 10 ? '0' + value : 'A' + (value - 10));
        }

        private static bool TryNormalizeServiceUuid(string serviceUuid, out Guid guid)
        {
            guid = Guid.Empty;
            if (string.IsNullOrWhiteSpace(serviceUuid))
            {
                return false;
            }

            var trimmed = serviceUuid.Trim();
            if (Guid.TryParse(trimmed, out guid))
            {
                return true;
            }

            if (IsHexString(trimmed))
            {
                if (trimmed.Length == 4 && ushort.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var shortUuid))
                {
                    guid = BluetoothUuidFrom16Bit(shortUuid);
                    return true;
                }
                if (trimmed.Length == 8 && uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var longUuid))
                {
                    guid = BluetoothUuidFrom32Bit(longUuid);
                    return true;
                }
            }

            return false;
        }

        private static Guid BluetoothUuidFrom16Bit(ushort shortUuid)
        {
            return BluetoothUuidFrom32Bit(shortUuid);
        }

        private static Guid BluetoothUuidFrom32Bit(uint shortUuid)
        {
            return new Guid($"0000{shortUuid:X4}-0000-1000-8000-00805F9B34FB");
        }
#endif

        private ResultObj BuildListenResult(string format, BleListenScanResult scanResult, byte[] keyBytes, BleCryptoOptions cryptoOptions)
        {
            var message = BuildListenMessage(format, scanResult, keyBytes, cryptoOptions);
            return new ResultObj { Success = true, Message = message };
        }

        private static string BuildListenMessage(string format, BleListenScanResult scanResult, byte[] keyBytes, BleCryptoOptions cryptoOptions)
        {
            var captures = scanResult.Captures;
            var sb = new StringBuilder();
            sb.AppendLine($"BLE listen captured {captures.Count} advertisement(s).");
            sb.AppendLine($"End reason: {DescribeListenEnd(scanResult.EndReason)}.");

            if (captures.Count == 0)
            {
                return sb.ToString().Trim();
            }

            format = BleCryptoHelper.NormalizeFormat(format, keyBytes.Length > 0);

            for (int i = 0; i < captures.Count; i++)
            {
                var capture = captures[i];
                sb.AppendLine();
                sb.AppendLine($"--- Capture {i + 1} ---");

                if (format == "victron")
                {
                    if (keyBytes.Length == 0)
                    {
                        sb.AppendLine(BuildOutputMessage(capture, null, "No key provided; skipping Victron decode."));
                    }
                    else if (TryDecodeVictron(capture, keyBytes, out var victronMessage, out var victronError))
                    {
                        sb.AppendLine(victronMessage);
                    }
                    else
                    {
                        sb.AppendLine(BuildOutputMessage(capture, null, victronError));
                    }
                }
                else
                {
                    if (!BleCryptoHelper.TryDecryptPayload(format, capture.Payload, keyBytes, cryptoOptions, out var plaintext, out var decryptError))
                    {
                        sb.AppendLine(BuildOutputMessage(capture, capture.Payload, $"Decryption failed; showing raw payload. {decryptError}"));
                    }
                    else
                    {
                        sb.AppendLine(BuildOutputMessage(capture, plaintext, null));
                    }
                }
            }

            return sb.ToString().Trim();
        }

        private static string DescribeListenEnd(string endReason)
        {
            return endReason switch
            {
                "capture_limit" => "capture limit reached",
                "raw_payload" => "raw payload provided",
                _ => "timeout"
            };
        }

        private static bool TryParseKey(string input, out byte[] keyBytes, out string error)
        {
            keyBytes = Array.Empty<byte>();
            error = "";

            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
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

            if (!TryExtractVictronRecord(capture.Payload, capture.PayloadType, out var record, out var extractError))
            {
                error = extractError;
                return false;
            }

            if (record.KeyCheck != keyBytes[0])
            {
                error = $"Victron key check mismatch (recordType=0x{record.RecordType:X2}, nonce=0x{record.Nonce:X4}, header=0x{record.KeyCheck:X2}, key[0]=0x{keyBytes[0]:X2}).";
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

        private static bool TryExtractVictronRecord(byte[] payload, string payloadType, out VictronRecord record, out string error)
        {
            record = default;
            error = "";

            if (payload.Length < 4)
            {
                error = "Victron payload too short.";
                return false;
            }

            ReadOnlySpan<byte> span = payload.AsSpan();

            if (string.Equals(payloadType, "raw", StringComparison.OrdinalIgnoreCase)
                && TryExtractManufacturerDataFromRawPayload(payload, out var manufacturerData))
            {
                span = manufacturerData;
            }

            if (span.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(span) == 0x02E1)
            {
                span = span.Slice(2);
            }

            if (span.Length < 4)
            {
                error = "Victron payload too short after company ID.";
                return false;
            }

            int offset;
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
            else
            {
                // Direct extra record starts at index 0.
                offset = 0;
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

            return true;
        }

        private static bool IsVictronInstantReadout(byte[] payload, string payloadType, byte keyFirstByte)
        {
            if (payload == null || payload.Length == 0)
            {
                return false;
            }

            ReadOnlySpan<byte> span = payload.AsSpan();
            if (string.Equals(payloadType, "raw", StringComparison.OrdinalIgnoreCase)
                && TryExtractManufacturerDataFromRawPayload(payload, out var manufacturerData))
            {
                span = manufacturerData;
            }

            if (span.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(span) == 0x02E1)
            {
                span = span.Slice(2);
            }

            if (span.Length < 4)
            {
                return false;
            }

            if (span[0] == 0x10)
            {
                if (span.Length < 8)
                {
                    return false;
                }

                byte recordType = span[4];
                if (recordType != 0x01)
                {
                    return false;
                }

                byte keyCheck = span[7];
                return keyCheck == keyFirstByte;
            }

            if (span[0] == 0x01)
            {
                byte keyCheck = span[3];
                return keyCheck == keyFirstByte;
            }

            return false;
        }

        private static string DescribeVictronPayload(byte[] payload, string payloadType)
        {
            ReadOnlySpan<byte> span = payload.AsSpan();
            if (string.Equals(payloadType, "raw", StringComparison.OrdinalIgnoreCase)
                && TryExtractManufacturerDataFromRawPayload(payload, out var manufacturerData))
            {
                span = manufacturerData;
            }

            if (span.Length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(span) == 0x02E1)
            {
                span = span.Slice(2);
            }

            if (span.Length == 0)
            {
                return $"payloadType={payloadType}, bytes=0";
            }

            byte packetType = span[0];
            string details = $"payloadType={payloadType}, packetType=0x{packetType:X2}, len={span.Length}";

            if (packetType == 0x10 && span.Length >= 8)
            {
                byte recordType = span[4];
                byte keyCheck = span[7];
                details += $", recordType=0x{recordType:X2}, keyCheck=0x{keyCheck:X2}";
            }
            else if (packetType == 0x01 && span.Length >= 4)
            {
                byte keyCheck = span[3];
                details += $", recordType=0x01, keyCheck=0x{keyCheck:X2}";
            }

            return details;
        }

        private static bool TryExtractManufacturerDataFromRawPayload(byte[] payload, out ReadOnlySpan<byte> manufacturerData)
        {
            manufacturerData = ReadOnlySpan<byte>.Empty;
            if (payload == null || payload.Length < 3)
            {
                return false;
            }

            int index = 0;
            while (index < payload.Length)
            {
                int length = payload[index];
                if (length == 0)
                {
                    break;
                }

                int typeIndex = index + 1;
                if (typeIndex >= payload.Length)
                {
                    break;
                }

                byte type = payload[typeIndex];
                int dataIndex = typeIndex + 1;
                int dataLength = length - 1;

                if (dataIndex + dataLength > payload.Length)
                {
                    break;
                }

                if (type == 0xFF && dataLength > 0)
                {
                    manufacturerData = payload.AsSpan(dataIndex, dataLength);
                    return true;
                }

                index += length + 1;
            }

            return false;
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
