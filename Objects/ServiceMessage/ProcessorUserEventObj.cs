namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ProcessorUserEventObj

    {
        private bool? _isLoggedInWebsite;
        private bool? _isHostsAdded;
        private string _authKey="";

        public bool? IsLoggedInWebsite { get => _isLoggedInWebsite; set => _isLoggedInWebsite = value; }
        public bool? IsHostsAdded { get => _isHostsAdded; set => _isHostsAdded = value; }
        public string AuthKey { get => _authKey; set => _authKey = value; }
        }
}