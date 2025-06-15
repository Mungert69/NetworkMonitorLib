using System.ComponentModel;
using System.Runtime.CompilerServices;
using NetworkMonitor.Objects.Repository;

namespace NetworkMonitor.Objects
{
    public class LocalProcessorStates : INotifyPropertyChanged, IRabbitListenerState
    {
        private bool _isRunning;
        private bool _isSetup;
        private bool _isConnectRunning;
        private bool _isRabbitConnected;
        private string _runningMessage = " Agent has not started running yet ";
        private string _setupMessage = " Agent setup not started yet ";
        private string _connectRunningMessage = " Agent has not started monitoring yet ";
        private string _rabbitSetupMessage = " RabbitMQ setup not started yet ";


        // Non Notified Fields
        public bool IsConnectRunning
        {
            get => _isConnectRunning;
            set => _isConnectRunning = value;
        }

        // Notified Fields.
        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public bool IsSetup
        {
            get => _isSetup;
            set => SetProperty(ref _isSetup, value);
        }



        private ConnectState _isConnectState = ConnectState.Error;

        public ConnectState IsConnectState
        {
            get => _isConnectState;
            set => SetProperty(ref _isConnectState, value);
        }

        public bool IsRabbitConnected
        {
            get => _isRabbitConnected;
            set => SetProperty(ref _isRabbitConnected, value);
        }

        public string SetupMessage
        {
            get => _setupMessage;
            set => SetProperty(ref _setupMessage, value);
        }

        public string RunningMessage
        {
            get => _runningMessage;
            set => SetProperty(ref _runningMessage, value);
        }

        public string RabbitSetupMessage
        {
            get => _rabbitSetupMessage;
            set => SetProperty(ref _rabbitSetupMessage, value);
        }

        public string ConnectRunningMessage
        {
            get => _connectRunningMessage;
            set => SetProperty(ref _connectRunningMessage, value);
        }
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
    public enum ConnectState
    {
        Running,
        Waiting,
        Error
    }
}
