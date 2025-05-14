using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Api.Services
{

    /// <summary>
    /// Object that contains details about the check.
    /// </summary>  
    public class DataObj
    {

        private string _setupFreeNetworkMonitor = $"To setup a service monitor visit : {AppConstants.FrontendUrl}/dashboard . You can Login to create a free account and then add hosts to monitor.";

        private string _testedAddress = "";
        private ushort? _testedPort = 0;

        private int? responseTime = -1;
        private bool _resultSuccess = false;
        private string _resultStatus = "";
        private string _checkPerformed = "";
        private int? _timeout = null;

        /// <summary>
        /// Indicates whether the check was successful.
        /// </summary>  
        public bool ResultSuccess { get => _resultSuccess; set => _resultSuccess = value; }

        /// <summary>
        /// Instructions for setting up a free network monitor.
        /// </summary>  
        public string ResultStatus { get => _resultStatus; set => _resultStatus = value; }

        /// <summary>
        /// The address of the tested host.
        /// </summary>  
        public string TestedAddress { get => _testedAddress; set => _testedAddress = value; }

        /// <summary>
        /// The port used for the check.
        /// </summary>  
        public ushort? TestedPort { get => _testedPort; set => _testedPort = value; }

        /// <summary>
        /// The time taken to perform the test in milliseconds. This indicates the response time of the host service. -1 indicates a timeout.
        /// </summary>  
        public int? ResponseTime { get => responseTime; set => responseTime = value; }

        /// <summary>
        /// Instructions for setting up a free network monitor.
        /// </summary>  
        public string SetupFreeNetworkMonitor { get => _setupFreeNetworkMonitor; set => _setupFreeNetworkMonitor = value; }

        /// <summary>
        /// The timeout value used in milliseconds.
        /// </summary>
        public int? Timeout { get => _timeout; set => _timeout = value; }
        public string CheckPerformed { get => _checkPerformed; set => _checkPerformed = value; }
    }


}
