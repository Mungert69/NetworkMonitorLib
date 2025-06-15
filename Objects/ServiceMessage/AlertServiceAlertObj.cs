using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.ServiceMessage
{

    public class AlertServiceAlertObj
    {
        public AlertServiceAlertObj(){}
      
        private string _appID="";
        private string _authKey="";
        private List<AlertFlagObj> _alertFlagObjs=new List<AlertFlagObj>();

        public string AppID { get => _appID; set => _appID = value; }
        public string AuthKey { get => _authKey; set => _authKey = value; }
        public List<AlertFlagObj> AlertFlagObjs { get => _alertFlagObjs; set => _alertFlagObjs = value; }
    }
}