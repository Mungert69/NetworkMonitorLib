using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Objects.Repository;

namespace NetworkMonitor.Objects
{

    // Define the interface that LocalCmdProcessorStates will implement
    public interface ILocalCmdProcessorStates : INotifyPropertyChanged
    {
        ConcurrentBag<MonitorIP> ActiveDevices { get; set; }
        ConcurrentBag<PingInfo> PingInfos { get; set; }
        string DefaultEndpointType { get; set; }
        List<string> EndpointTypes { get; set; }
        bool UseDefaultEndpointType { get; set; }
        bool UseFastScan { get; set; }
        bool LimitPorts { get; set; }
        List<MonitorIP> SelectedDevices { get; set; }
        NetworkInterfaceInfo SelectedNetworkInterface { get; set; }
        List<NetworkInterfaceInfo> AvailableNetworkInterfaces { get; set; }
        string CompletedMessage { get; set; }
        string RunningMessage { get; set; }
        bool IsRunning { get; set; }
        bool IsSuccess { get; set; }
        bool IsCmdRunning { get; set; }
        bool IsCmdSuccess { get; set; }
        bool IsCmdAvailable { get; set; }
        string CmdName { get; set; }
        string CmdDisplayName { get; set; }


        event Func<Task>? OnStartScanAsync;
        event Func<Task>? OnCancelScanAsync;
        event Func<Task>? OnAddServicesAsync;

        Task Scan();
        Task Cancel();
        Task AddServices();
        void Init();
    }

    


    public class LocalCmdProcessorStates : ILocalCmdProcessorStates
    {


        ConcurrentBag<MonitorIP> _activeDevices = new ConcurrentBag<MonitorIP>();
        List<MonitorIP> _selectedDevices = new List<MonitorIP>();
        ConcurrentBag<PingInfo> _pingInfos = new ConcurrentBag<PingInfo>();
        public event Func<Task>? OnStartScanAsync;
        public event Func<Task>? OnCancelScanAsync;
        public event Func<Task>? OnAddServicesAsync;
        private bool _isRunning;
        private bool _isSuccess;
        private bool _isCmdRunning;
        private bool _isCmdSuccess;

        private string _defaultEndpointType = "imcp";
        private bool _useDefaultEndpointType = false;
        private bool _useFastScan = false;
        private bool _limitPorts = false;
        private bool _isCmdAvailable = true;
        private string _cmdName = "";
        private string _cmdDisplayName = "";
        private List<string> _endpointTypes = EndPointTypeFactory.GetInternalTypes();

        private string _runningMessage = "Scanner starting...\n";
        private string _completedMessage = "";
        private NetworkInterfaceInfo _selectedNetworkInterface;
        public NetworkInterfaceInfo SelectedNetworkInterface
        {
            get => _selectedNetworkInterface;
            set
            {
                _selectedNetworkInterface = value;
                OnPropertyChanged();
            }
        }

        public List<NetworkInterfaceInfo> AvailableNetworkInterfaces { get; set; } = new List<NetworkInterfaceInfo>();

        public LocalCmdProcessorStates(string cmdName, string cmdDisplayName)
        {
            CmdName = cmdName;
            CmdDisplayName = cmdDisplayName;
        }
        public void Init()
        {
            _activeDevices = new ConcurrentBag<MonitorIP>();
            _pingInfos = new ConcurrentBag<PingInfo>();
            _selectedDevices = new List<MonitorIP>();

            _isRunning = false;
            _isSuccess = false;
            RunningMessage = "Scanner starting...\n";
            _completedMessage = "";
        }

        public async Task Scan()
        {
            Init();
            if (OnStartScanAsync != null)
            {
                await OnStartScanAsync();
            }

        }

        public async Task Cancel()
        {

            if (OnCancelScanAsync != null)
            {
                await OnCancelScanAsync();
            }

        }

        public async Task AddServices()
        {

            if (OnAddServicesAsync != null)
            {
                await OnAddServicesAsync();
            }

        }


        // Notified Fields.
        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public bool IsSuccess
        {
            get => _isSuccess;
            set => SetProperty(ref _isSuccess, value);
        }





        public string CompletedMessage
        {
            get => _completedMessage;
            set => SetProperty(ref _completedMessage, value);
        }

        public string RunningMessage
        {
            get => _runningMessage;
            set => SetProperty(ref _runningMessage, value);
        }
        public ConcurrentBag<MonitorIP> ActiveDevices { get => _activeDevices; set => _activeDevices = value; }
        public ConcurrentBag<PingInfo> PingInfos { get => _pingInfos; set => _pingInfos = value; }
        public string DefaultEndpointType
        {
            get => _defaultEndpointType;
            set => SetProperty(ref _defaultEndpointType, value);
        }

        public List<string> EndpointTypes
        {
            get => _endpointTypes;
            set => SetProperty(ref _endpointTypes, value);
        }

        public bool UseDefaultEndpointType
        {
            get => _useDefaultEndpointType;
            set => SetProperty(ref _useDefaultEndpointType, value);
        }
        public bool UseFastScan
        {
            get => _useFastScan;
            set => SetProperty(ref _useFastScan, value);
        }
        public bool LimitPorts
        {
            get => _limitPorts;
            set => SetProperty(ref _limitPorts, value);
        }
        public List<MonitorIP> SelectedDevices { get => _selectedDevices; set => _selectedDevices = value; }
        public bool IsCmdAvailable { get => _isCmdAvailable; set => _isCmdAvailable = value; }
        public string CmdName { get => _cmdName; set => _cmdName = value; }
        public string CmdDisplayName { get => _cmdDisplayName; set => _cmdDisplayName = value; }
        public bool IsCmdRunning { get => _isCmdRunning; set => _isCmdRunning = value; }
        public bool IsCmdSuccess { get => _isCmdSuccess; set => _isCmdSuccess = value; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

}
