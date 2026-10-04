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
            var selectedDecoder = BlePayloadDecoderRegistry.Default.Find(format);
            string payloadMode = parsed.GetString("payload", selectedDecoder?.DefaultPayloadMode ?? "manufacturer");
            int manufacturerId = parsed.GetInt("manufacturer_id", -1);
            string serviceUuid = parsed.GetString("service_uuid", selectedDecoder?.ServiceUuid ?? "");
            int maxCaptures = parsed.GetInt("max_captures", DefaultMaxCaptures);
            string rawPayload = parsed.GetString("raw_payload");

            string normalizedAddress = "";

            if (!BleKeyParser.TryParse(keyRaw, selectedDecoder, out var keyBytes, out var keyError))
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
            if (string.Equals(_netConfig.OSPlatform, "linux", StringComparison.OrdinalIgnoreCase))
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
            if (!string.Equals(_netConfig.OSPlatform, "android", StringComparison.OrdinalIgnoreCase))
            {
                return new ResultObj { Success = false, Message = "BLE broadcast listen processor is only available on Android or Windows." };
            }

            try
            {
                format = format.Trim().ToLowerInvariant();
                if (BlePayloadDecoderRegistry.Default.Find(format)?.ManufacturerId is int defaultManufacturerId && manufacturerId == -1)
                {
                    manufacturerId = defaultManufacturerId;
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
                        BlePayloadDecoderRegistry.Default.Find(format) is { } filterDecoder
                            && (!filterDecoder.RequiresKey || keyBytes.Length > 0) ? filterDecoder : null,
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
            IBlePayloadDecoder? packetDecoder,
            byte keyFirstByte)
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
                packetDecoder,
                keyFirstByte,
                maxCaptures,
                captures,
                tcs,
                _logger);

#pragma warning disable CS8602
            var settings = new ScanSettings.Builder()
                .SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)
                .Build();
#pragma warning restore CS8602

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
                // Match service data itself; BTHome need not advertise a separate UUID list.
                builder.SetServiceData(new ParcelUuid(uuid), Array.Empty<byte>());
                hasFilter = true;
            }

            if (hasFilter)
            {
                var filter = builder.Build();
                if (filter != null)
                {
                    filters.Add(filter);
                }
            }

            return filters;
        }

        private sealed class BleListenScanCallback : ScanCallback
        {
            private readonly string _targetAddress;
            private readonly string _payloadMode;
            private readonly int _manufacturerId;
            private readonly string _serviceUuid;
            private readonly IBlePayloadDecoder? _packetDecoder;
            private readonly byte _keyFirstByte;
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
                IBlePayloadDecoder? packetDecoder,
                byte keyFirstByte,
                int maxCaptures,
                List<BleCapture> captures,
                TaskCompletionSource<string> tcs,
                ILogger logger)
            {
                _targetAddress = targetAddress ?? "";
                _payloadMode = (payloadMode ?? "manufacturer").Trim().ToLowerInvariant();
                _manufacturerId = manufacturerId;
                _serviceUuid = serviceUuid ?? "";
                _packetDecoder = packetDecoder;
                _keyFirstByte = keyFirstByte;
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

                if (_packetDecoder != null && !_packetDecoder.Accepts(payload, payloadType, _keyFirstByte))
                {
                    _logger.LogDebug("Ignoring packet rejected by protocol decoder. {Details}", _packetDecoder.Describe(payload, payloadType));
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
                return Array.Empty<byte>();
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
                if (BlePayloadDecoderRegistry.Default.Find(format)?.ManufacturerId is int defaultManufacturerId && manufacturerId == -1)
                {
                    manufacturerId = defaultManufacturerId;
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
                        BlePayloadDecoderRegistry.Default.Find(format) is { } filterDecoder
                            && (!filterDecoder.RequiresKey || keyBytes.Length > 0) ? filterDecoder : null,
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
            IBlePayloadDecoder? packetDecoder,
            byte keyFirstByte)
        {
            _ = address;
            _ = payloadMode;
            _ = manufacturerId;
            _ = serviceUuid;
            _ = maxCaptures;
            _ = cancellationToken;
            _ = packetDecoder;
            _ = keyFirstByte;

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
            if (!string.Equals(_netConfig.OSPlatform, "windows", StringComparison.OrdinalIgnoreCase))
            {
                return new ResultObj { Success = false, Message = "BLE broadcast listen processor is only available on Android or Windows." };
            }

            try
            {
                format = format.Trim().ToLowerInvariant();
                if (BlePayloadDecoderRegistry.Default.Find(format)?.ManufacturerId is int defaultManufacturerId && manufacturerId == -1)
                {
                    manufacturerId = defaultManufacturerId;
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
                        BlePayloadDecoderRegistry.Default.Find(format) is { } filterDecoder
                            && (!filterDecoder.RequiresKey || keyBytes.Length > 0) ? filterDecoder : null,
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
            IBlePayloadDecoder? packetDecoder,
            byte keyFirstByte)
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

                if (packetDecoder != null && !packetDecoder.Accepts(payload, payloadType, keyFirstByte))
                {
                    _logger.LogDebug("Ignoring packet rejected by protocol decoder. {Details}", packetDecoder.Describe(payload, payloadType));
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
                System.Buffer.BlockCopy(data, 0, result, 2, data.Length);
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
                    continue;
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
                System.Buffer.BlockCopy(data, 0, payload, 2, data.Length);
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

            format = BlePayloadDecoderRegistry.Default.Find(format)?.Format
                ?? BleCryptoHelper.NormalizeFormat(format, keyBytes.Length > 0);

            for (int i = 0; i < captures.Count; i++)
            {
                var capture = captures[i];
                sb.AppendLine();
                sb.AppendLine($"--- Capture {i + 1} ---");

                if (BlePayloadDecoderRegistry.Default.Find(format) is { } decoder)
                {
                    if (decoder.RequiresKey && keyBytes.Length == 0)
                    {
                        sb.AppendLine(BuildOutputMessage(capture, null, $"No key provided; skipping {decoder.Format} decode."));
                    }
                    else if (decoder.TryDecode(new BlePayload(capture.Address, capture.PayloadType, capture.Payload), keyBytes, out var decodedMessage, out var decodeError))
                    {
                        sb.AppendLine(decodedMessage);
                    }
                    else
                    {
                        sb.AppendLine(BuildOutputMessage(capture, null, decodeError));
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

        private static bool TryParseKey(string input, out byte[] keyBytes, out string error) =>
            BleKeyParser.TryParse(input, null, out keyBytes, out error);

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

        // Compatibility hooks for existing packet fixtures; protocol implementation lives in Ble/.
        private static bool IsVictronInstantReadout(byte[] payload, string payloadType, byte keyFirstByte) =>
            VictronPayloadDecoder.IsVictronInstantReadout(payload, payloadType, keyFirstByte);

        private static bool TryExtractVictronRecord(byte[] payload, string payloadType,
            out VictronPayloadDecoder.VictronRecord record, out string error) =>
            VictronPayloadDecoder.TryExtractVictronRecord(payload, payloadType, out record, out error);

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
