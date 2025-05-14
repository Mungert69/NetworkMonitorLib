

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ProcessorInitObj

    {
        public ProcessorInitObj(){}
        private List<MonitorIP> _monitorIPs=new List<MonitorIP>();

        private List<MonitorPingInfo> _savedMonitorPingInfos=new List<MonitorPingInfo>();

        private PingParams? _pingParams;
        private bool _isProcessorReady = false;
        private bool _reset=false;
        private bool _totalReset=false;
        private string _appID="";
        private string _authKey="";

        private string _rabbitHostName="";
        private int _rabbitPort;

        public List<MonitorIP> MonitorIPs { get => _monitorIPs; set => _monitorIPs = value; }
        public List<MonitorPingInfo> SavedMonitorPingInfos { get => _savedMonitorPingInfos; set => _savedMonitorPingInfos = value; }
        public PingParams? PingParams { get => _pingParams; set => _pingParams = value; }
        public bool IsProcessorReady { get => _isProcessorReady; set => _isProcessorReady = value; }
        public bool Reset { get => _reset; set => _reset = value; }
        public bool TotalReset { get => _totalReset; set => _totalReset = value; }
        public string AppID { get => _appID; set => _appID = value; }
        public string AuthKey { get => _authKey; set => _authKey = value; }
        public string RabbitHostName { get => _rabbitHostName; set => _rabbitHostName = value; }
        public int RabbitPort { get => _rabbitPort; set => _rabbitPort = value; }
    }
}