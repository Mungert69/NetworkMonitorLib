using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class SwapMonitorPingInfo
    {
        public SwapMonitorPingInfo(){}
        private int _iD;
        private string _appID="";

        public int ID { get => _iD; set => _iD = value; }
        public string AppID { get => _appID; set => _appID = value; }
    }
}