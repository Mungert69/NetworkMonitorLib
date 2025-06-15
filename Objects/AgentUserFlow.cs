using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetworkMonitor.Objects
{
    public class AgentUserFlow : INotifyPropertyChanged
    {
        private bool _isAuthorized = false;
        private bool _isLoggedInWebsite = false;
        private bool _isHostsAdded = false;
        private bool _isChatOpened = false;

        public AgentUserFlow() { }

        public AgentUserFlow(bool isAuthorized, bool isLoggedInWebsite, bool isHostsAdded, bool isChatOpened)
        {
            _isAuthorized = isAuthorized;
            _isLoggedInWebsite = isLoggedInWebsite;
            _isHostsAdded = isHostsAdded;
            IsChatOpened = isChatOpened;
        }

        public bool IsAuthorized
        {
            get => _isAuthorized;
            set => SetProperty(ref _isAuthorized, value);
        }

        public bool IsLoggedInWebsite
        {
            get => _isLoggedInWebsite;
            set => SetProperty(ref _isLoggedInWebsite, value);
        }

        public bool IsHostsAdded
        {
            get => _isHostsAdded;
            set => SetProperty(ref _isHostsAdded, value);
        }
        public bool IsChatOpened
        {
            get => _isChatOpened;
            set => SetProperty(ref _isChatOpened, value);
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
}
