using NetworkMonitor.Objects;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using RestSharp;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects.Factory;
namespace NetworkMonitor.Utils.Helpers
{

    public class LocalSystemParamsHelper : ISystemParamsHelper
    {


        private SystemUrl _thisSystemUrl;
        public LocalSystemParamsHelper(SystemUrl thisSystemUrl)
        {
            _thisSystemUrl = thisSystemUrl;

        }
        public string GetPublicIP()
        {
            return "";
        }
        public SystemParams GetSystemParams()
        {
            SystemParams systemParams = new SystemParams();

            systemParams.ThisSystemUrl = _thisSystemUrl;
            return systemParams;
        }

        public PingParams GetPingParams()
        {

            return new PingParams();

        }
        public AlertParams GetAlertParams()
        {
            return new AlertParams();
        }
        public MLParams GetMLParams() {
            return new MLParams();
        }
    }

}
