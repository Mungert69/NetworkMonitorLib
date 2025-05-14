using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class HostObject : IConnectionObject
    {
        private string _address = "";
        private ushort _port = 0;
        private ushort _timeout = 0;
        private string _endPointType="";

        /// <summary>
        /// This field is required.
        /// Address of the host.
        /// </summary>

        public string Address { get => _address; set => _address = value; }
        /// <summary>
        /// This field is optional.
        /// Port used by the host service to test. Set to zero to use the default for the end point type.
        /// </summary>
        public ushort Port { get => _port; set => _port = value; }
        /// <summary>
        /// This field is optional.
        /// The timeout in milliseconds to be used when testing this host service. The default is not included is to set the timeout to 10000 ms.
        /// </summary>
        public ushort Timeout { get => _timeout; set => _timeout = value; }
        public string EndPointType { get => _endPointType; set => _endPointType = value; }
    }
}