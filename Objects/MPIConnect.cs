using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class MPIConnect
    {
          public MPIConnect(){}
        public string Message { get; set; } = "";
        public bool IsUp { get; set; } = false;
        public DateTime EventTime { get; set; } = DateTime.UtcNow;
        //public Boolean Down { get; set; } 
        public string? SiteHash { get; set; } = null;


       
        public PingInfo _pingInfo = new PingInfo();
        public PingInfo PingInfo
        {
            get
            {
   
                return _pingInfo;
            }
            set
            {
                _pingInfo = value;
            }
        }
    }
}