using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{

    public interface IAlertable
    {
        int ID { get; set; }
        string? UserID { get; set; }
        string? Address { get; set; }
        string? UserName { get; set; }
        string? AppID { get; set; }
        string? EndPointType { get; set; }
        int Timeout { get; set; }
        string? AddUserEmail { get; set; }
        bool IsEmailVerified { get; set; }
        // Assuming these properties come from StatusObj and are relevant
        bool AlertFlag { get; set; }
        bool AlertSent { get; set; }
        int DownCount { get; set; }
        DateTime? EventTime { get; set; }
        bool? IsUp { get; set; }
        string? Message { get; set; }
        int MonitorPingInfoID { get; set; }
    }

    public class MonitorStatusAlert : StatusObj, IAlertable
    {
        public MonitorStatusAlert() { }
        private string? _userID;
        private string? _address;
        private string? _userName;

        private string? _appID;
        private string? _endPointType;
        private int _timeout;
        private string? _addUserEmail;
        private bool _isEmailVerified = false;




        public MonitorStatusAlert(IAlertable m)
        {
            ID = m.ID;
            AppID = m.AppID;
            Address = m.Address;
            AlertFlag = m.AlertFlag;
            AlertSent = m.AlertSent;
            DownCount = m.DownCount;
            EndPointType = m.EndPointType;
            EventTime = m.EventTime;
            IsUp = m.IsUp;
            UserName = m.UserName;
            UserID = m.UserID;
            Timeout = m.Timeout;
            MonitorPingInfoID = m.MonitorPingInfoID;
            Message = m.Message;
            AddUserEmail = m.AddUserEmail;
            IsEmailVerified = m.IsEmailVerified;
        }

        public string? UserID { get => _userID; set => _userID = value; }
        public string? Address { get => _address; set => _address = value; }
        public string? UserName { get => _userName; set => _userName = value; }
        public string? AppID { get => _appID; set => _appID = value; }
        public string? EndPointType { get => _endPointType; set => _endPointType = value; }
        public int Timeout { get => _timeout; set => _timeout = value; }
        public string? AddUserEmail { get => _addUserEmail; set => _addUserEmail = value; }
        public bool IsEmailVerified { get => _isEmailVerified; set => _isEmailVerified = value; }
    }

    public class PredictStatusAlert : PredictStatus, IAlertable
    {
        public PredictStatusAlert() { }
        private string? _userID;
        private string? _address;
        private string? _userName;

        private string? _appID;
        private string? _endPointType;
        private int _timeout;
        private string? _addUserEmail;
        private bool _isEmailVerified = false;




        public PredictStatusAlert(IAlertable m)
        {
            ID = m.ID;
            AppID = m.AppID;
            Address = m.Address;
            AlertFlag = m.AlertFlag;
            AlertSent = m.AlertSent;
            DownCount = m.DownCount;
            EndPointType = m.EndPointType;
            EventTime = m.EventTime;
            IsUp = m.IsUp;
            UserName = m.UserName;
            UserID = m.UserID;
            Timeout = m.Timeout;
            MonitorPingInfoID = m.MonitorPingInfoID;
            Message = m.Message;
            AddUserEmail = m.AddUserEmail;
            IsEmailVerified = m.IsEmailVerified;
            // Additional fields copied if m is actually a PredictStatusAlert
            if (m is PredictStatusAlert psa)
            {
                ChangeDetectionResult = psa.ChangeDetectionResult;
                SpikeDetectionResult = psa.SpikeDetectionResult;
                // Copy any other PredictStatusAlert-specific properties here
            }
        }

        public PredictStatusAlert(PredictStatusAlert m)
        {
            ID = m.ID;
            AppID = m.AppID;
            Address = m.Address;
            AlertFlag = m.AlertFlag;
            AlertSent = m.AlertSent;
            DownCount = m.DownCount;
            EndPointType = m.EndPointType;
            EventTime = m.EventTime;
            IsUp = m.IsUp;
            UserName = m.UserName;
            UserID = m.UserID;
            Timeout = m.Timeout;
            MonitorPingInfoID = m.MonitorPingInfoID;
            Message = m.Message;
            AddUserEmail = m.AddUserEmail;
            IsEmailVerified = m.IsEmailVerified;
            ChangeDetectionResult = m.ChangeDetectionResult;
            SpikeDetectionResult = m.SpikeDetectionResult;
        }

        public string? UserID { get => _userID; set => _userID = value; }
        public string? Address { get => _address; set => _address = value; }
        public string? UserName { get => _userName; set => _userName = value; }
        public string? AppID { get => _appID; set => _appID = value; }
        public string? EndPointType { get => _endPointType; set => _endPointType = value; }
        public int Timeout { get => _timeout; set => _timeout = value; }
        public string? AddUserEmail { get => _addUserEmail; set => _addUserEmail = value; }
        public bool IsEmailVerified { get => _isEmailVerified; set => _isEmailVerified = value; }
    }
}