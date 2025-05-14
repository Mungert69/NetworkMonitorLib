
namespace NetworkMonitor.Objects.ServiceMessage
{

    public class MonitorServiceInitObj
    {
        public MonitorServiceInitObj(){}
        private bool _isServiceReady;
        private bool _isMonitorCheckServiceReady;
        private bool _initResetProcessor;
        private bool _initTotalResetProcesser;
        private bool _initUpdateAlertMessage;
        private bool _initTotalResetAlertMessage;

        public bool InitResetProcessor { get => _initResetProcessor; set => _initResetProcessor = value; }
        public bool InitTotalResetProcesser { get => _initTotalResetProcesser; set => _initTotalResetProcesser = value; }
        public bool InitTotalResetAlertMessage { get => _initTotalResetAlertMessage; set => _initTotalResetAlertMessage = value; }
        public bool InitUpdateAlertMessage { get => _initUpdateAlertMessage; set => _initUpdateAlertMessage = value; }
        public bool IsServiceReady { get => _isServiceReady; set => _isServiceReady = value; }
        public bool IsMonitorCheckServiceReady { get => _isMonitorCheckServiceReady; set => _isMonitorCheckServiceReady = value; }
    }
}