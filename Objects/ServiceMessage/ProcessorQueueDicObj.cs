
namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ProcessorQueueDicObj
    {
        public ProcessorQueueDicObj(){}
        private List<UpdateMonitorIP> _monitorIPs= new List<UpdateMonitorIP>();
        private string _userId="";
        private string _authKey="";


        public List<UpdateMonitorIP> MonitorIPs { get => _monitorIPs; set => _monitorIPs = value; }
        public string UserId { get => _userId; set => _userId = value; }
        public string AuthKey { get => _authKey; set => _authKey = value; }
    }
}