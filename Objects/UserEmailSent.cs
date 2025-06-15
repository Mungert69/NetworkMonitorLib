
using System;

namespace NetworkMonitor.Objects
{
    public class UserEmailSent
    {
        public UserEmailSent(){}
       private string _userID="";
       private bool _isVerifyEmail;
       private bool _isAlertEmail;
       private DateTime _dateSent;

        public string UserID { get => _userID; set => _userID = value; }
        public DateTime DateSent { get => _dateSent; set => _dateSent = value; }
        public bool IsVerifyEmail { get => _isVerifyEmail; set => _isVerifyEmail = value; }
        public bool IsAlertEmail { get => _isAlertEmail; set => _isAlertEmail = value; }
    }
}
