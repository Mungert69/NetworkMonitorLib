namespace NetworkMonitor.Connection;

/// <summary>One platform scan registration, independent of individual endpoint executions.</summary>
public sealed class PlatformBleAdvertisementSource : IBleAdvertisementSource
{
#if WINDOWS
    private Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher? _watcher;
#elif ANDROID
    private Android.Bluetooth.LE.BluetoothLeScanner? _scanner;
    private Callback? _callback;
#endif
    public void Start(Action<string, byte[]> receive, Action<string> failed)
    {
#if WINDOWS
        if (_watcher != null) return;
        var watcher = new Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher
        {
            ScanningMode = Windows.Devices.Bluetooth.Advertisement.BluetoothLEScanningMode.Passive
        };
        watcher.Received += (_, args) =>
        {
            var raw = new List<byte>();
            foreach (var manufacturer in args.Advertisement.ManufacturerData)
            {
                var bytes = new byte[manufacturer.Data.Length];
                using var reader = Windows.Storage.Streams.DataReader.FromBuffer(manufacturer.Data);
                reader.ReadBytes(bytes);
                if (bytes.Length > 252) continue;
                raw.Add((byte)(bytes.Length + 3)); raw.Add(0xff);
                raw.Add((byte)(manufacturer.CompanyId & 0xff)); raw.Add((byte)(manufacturer.CompanyId >> 8));
                raw.AddRange(bytes);
            }
            foreach (var section in args.Advertisement.DataSections)
            {
                if (section.DataType == 0xff && args.Advertisement.ManufacturerData.Count > 0) continue;
                var data = new byte[section.Data.Length];
                using var reader = Windows.Storage.Streams.DataReader.FromBuffer(section.Data);
                reader.ReadBytes(data);
                if (data.Length > 254) continue;
                raw.Add((byte)(data.Length + 1)); raw.Add(section.DataType); raw.AddRange(data);
            }
            string hex = args.BluetoothAddress.ToString("X12");
            string address = string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
            receive(address, raw.ToArray());
        };
        watcher.Stopped += (_, args) =>
        {
            if (ReferenceEquals(_watcher, watcher)) failed($"BLE scanner stopped: {args.Error}");
        };
        _watcher = watcher;
        watcher.Start();
#elif ANDROID
        if (_scanner != null) return;
        var manager = (Android.Bluetooth.BluetoothManager?)Android.App.Application.Context
            .GetSystemService(Android.Content.Context.BluetoothService);
        var adapter = manager?.Adapter;
        if (adapter == null || !adapter.IsEnabled) throw new InvalidOperationException("Bluetooth adapter is disabled or missing.");
        _scanner = adapter.BluetoothLeScanner ?? throw new InvalidOperationException("Bluetooth LE scanner not available.");
        _callback = new Callback(receive, failed);
        using var builder = new Android.Bluetooth.LE.ScanSettings.Builder();
        using var settings = builder.SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency)!.Build();
        _scanner.StartScan(new List<Android.Bluetooth.LE.ScanFilter>(), settings, _callback);
#else
        throw new NotSupportedException("BLE scan on Linux requires BlueZ/D-Bus integration and privileged access to the host BLE adapter. Continuous BLE scanning is currently supported on Android and Windows builds.");
#endif
    }
    public void Stop()
    {
#if WINDOWS
        var watcher = _watcher; _watcher = null; watcher?.Stop();
#elif ANDROID
        if (_scanner != null && _callback != null) _scanner.StopScan(_callback);
        _callback?.Dispose(); _callback = null; _scanner = null;
#endif
    }
    public void Dispose() => Stop();
#if ANDROID
    private sealed class Callback(Action<string, byte[]> receive, Action<string> failed) : Android.Bluetooth.LE.ScanCallback
    {
        public override void OnScanResult(Android.Bluetooth.LE.ScanCallbackType callbackType, Android.Bluetooth.LE.ScanResult? result)
        {
            var bytes = result?.ScanRecord?.GetBytes();
            var address = result?.Device?.Address;
            if (address != null && bytes != null) receive(address, bytes);
        }
        public override void OnBatchScanResults(IList<Android.Bluetooth.LE.ScanResult>? results)
        {
            if (results != null) foreach (var result in results) OnScanResult(Android.Bluetooth.LE.ScanCallbackType.AllMatches, result);
        }
        public override void OnScanFailed(Android.Bluetooth.LE.ScanFailure errorCode) => failed($"BLE scan failed: {errorCode}");
    }
#endif
}
