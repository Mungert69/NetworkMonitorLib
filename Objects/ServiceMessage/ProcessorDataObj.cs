

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ProcessorDataObj : IDisposable
    {
        public ProcessorDataObj(){
            
        }
        private string _appID="";
        private string _rabbitPassword;
        private string _authKey="";
        private uint _piIDKey;
        private bool _resetPingInfos;
        private List<PingInfo> _pingInfos= new List<PingInfo>();
        private List<RemovePingInfo> _removePingInfos=new List<RemovePingInfo>();
        private List<MonitorPingInfo> _monitorPingInfos=new List<MonitorPingInfo>();
        private List<MonitorIP> _monitorIPs = new List<MonitorIP>();
        private List<int> _removeMonitorPingInfoIDs=new List<int>();
        private List<SwapMonitorPingInfo> _swapMonitorPingInfos=new List<SwapMonitorPingInfo>();
        private List<string> _statusList=new List<string>();

        private List<MonitorStatusAlert> _monitorStatusAlerts=new List<MonitorStatusAlert>();
        private List<PredictStatusAlert> _predictStatusAlerts=new List<PredictStatusAlert>();

        public List<PingInfo> PingInfos { get => _pingInfos; set => _pingInfos = value; }
        public List<MonitorPingInfo> MonitorPingInfos { get => _monitorPingInfos; set => _monitorPingInfos = value; }
        public List<int> RemoveMonitorPingInfoIDs { get => _removeMonitorPingInfoIDs; set => _removeMonitorPingInfoIDs = value; }
        public List<SwapMonitorPingInfo> SwapMonitorPingInfos{get => _swapMonitorPingInfos; set => _swapMonitorPingInfos=value;}
        public string AppID { get => _appID; set => _appID = value; }
        public bool ResetPingInfos { get => _resetPingInfos; set => _resetPingInfos = value; }
        public List<MonitorStatusAlert> MonitorStatusAlerts { get => _monitorStatusAlerts; set => _monitorStatusAlerts = value; }
        public List<RemovePingInfo> RemovePingInfos { get => _removePingInfos; set => _removePingInfos = value; }
        public uint PiIDKey { get => _piIDKey; set => _piIDKey = value; }
        public List<string> StatusList { get => _statusList; set => _statusList = value; }
        public string AuthKey { get => _authKey; set => _authKey = value; }
        public List<PredictStatusAlert> PredictStatusAlerts { get => _predictStatusAlerts; set => _predictStatusAlerts = value; }
        public List<MonitorIP> MonitorIPs { get => _monitorIPs; set => _monitorIPs = value; }
        public string RabbitPassword { get => _rabbitPassword; set => _rabbitPassword = value; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _monitorPingInfos = new List<MonitorPingInfo>();
                _monitorStatusAlerts = new List<MonitorStatusAlert>();
                _pingInfos = new List<PingInfo>();
                _removePingInfos = new List<RemovePingInfo>();
            }
            // free native resources if there are any.
        }
    }
}