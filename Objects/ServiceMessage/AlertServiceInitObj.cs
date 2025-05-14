using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class AlertServiceInitObj
    {
        public AlertServiceInitObj(){}
        private bool _isAlertServiceReady;
        private bool _totalReset=false;
        private bool _updateUserInfos=false;

        private List<UserInfo> _userInfos=new List<UserInfo>();

        public bool IsAlertServiceReady { get => _isAlertServiceReady; set => _isAlertServiceReady = value; }
        public bool TotalReset { get => _totalReset; set => _totalReset = value; }
        public bool UpdateUserInfos { get => _updateUserInfos; set => _updateUserInfos = value; }
        public List<UserInfo> UserInfos { get => _userInfos; set => _userInfos = value; }
    }
}