using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class RemovePingInfo
    {
        public RemovePingInfo(){}
        public ulong ID{get;set;}
        public int MonitorPingInfoID{get;set;}
    }
}