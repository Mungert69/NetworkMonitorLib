using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.ServiceMessage
{
 

    public class MonitorDataInitObj
    {
        public MonitorDataInitObj(){}
        private bool _isDataReady;
         private bool _isDataSaveReady;
         private bool _isDataPurgeReady;
          private bool _isDataMessage;
         private bool _isDataSaveMessage;
         private bool _isDataPurgeMessage;

        private bool _initResetProcessor;
        private bool _initTotalResetProcesser;
        private bool _initUpdateAlertMessage;
        private bool _initTotalResetAlertMessage;
        private bool _isTestMode=false;

        public bool InitResetProcessor { get => _initResetProcessor; set => _initResetProcessor = value; }
        public bool InitTotalResetProcesser { get => _initTotalResetProcesser; set => _initTotalResetProcesser = value; }
        public bool InitTotalResetAlertMessage { get => _initTotalResetAlertMessage; set => _initTotalResetAlertMessage = value; }
        public bool InitUpdateAlertMessage { get => _initUpdateAlertMessage; set => _initUpdateAlertMessage = value; }
        public bool IsDataReady { get => _isDataReady; set => _isDataReady = value; }
        public bool IsDataSaveReady { get => _isDataSaveReady; set => _isDataSaveReady = value; }
        public bool IsDataPurgeReady { get => _isDataPurgeReady; set => _isDataPurgeReady = value; }
        public bool IsDataMessage { get => _isDataMessage; set => _isDataMessage = value; }
        public bool IsDataSaveMessage { get => _isDataSaveMessage; set => _isDataSaveMessage = value; }
        public bool IsDataPurgeMessage { get => _isDataPurgeMessage; set => _isDataPurgeMessage = value; }
        public bool IsTestMode { get => _isTestMode; set => _isTestMode = value; }
    }
}