using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class UpdateMonitorIP : MonitorIP
    {
     
        private MonitorPingInfo? _monitorPingInfo=new MonitorPingInfo();
        private bool _deleteAll;
        private bool _isSwapping;
        private bool _delete;

        public UpdateMonitorIP()
        {
        }

        public UpdateMonitorIP(MonitorIP m)
        {
            AppID=m.AppID;
            Address=m.Address;
            Enabled=m.Enabled;
            EndPointType= m.EndPointType;
            Hidden= m.Hidden;
            ID= m.ID;
            Timeout= m.Timeout;
            UserID= m.UserID;
            Port= m.Port;
            AddUserEmail=m.AddUserEmail;
            IsEmailVerified=m.IsEmailVerified;
            Username=m.Username;
            Password=m.Password;
        }

        public MonitorPingInfo? MonitorPingInfo { get => _monitorPingInfo; set => _monitorPingInfo = value; }
        public bool DeleteAll { get => _deleteAll; set => _deleteAll = value; }
        public bool IsSwapping { get => _isSwapping; set => _isSwapping = value; }
        public bool Delete { get => _delete; set => _delete = value; }
    }
}