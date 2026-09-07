
namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ProcessorQueueDicObj : IBackendSignedMessage
    {
        public ProcessorQueueDicObj() { }
        private List<UpdateMonitorIP> _monitorIPs = new List<UpdateMonitorIP>();
        private string _userId = "";
        private string _authKey = "";
        private string _backendSignature = "";


        public List<UpdateMonitorIP> MonitorIPs { get => _monitorIPs; set => _monitorIPs = value; }
        public string UserId { get => _userId; set => _userId = value; }
        public string AuthKey { get => _authKey; set => _authKey = value; }
        public string BackendSignature { get => _backendSignature; set => _backendSignature = value; }
    }
}
