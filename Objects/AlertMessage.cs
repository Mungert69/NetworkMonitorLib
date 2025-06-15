
namespace NetworkMonitor.Objects
{
    public class AlertMessage
    {
        public  AlertMessage(){}
        private UserInfo _userInfo=new UserInfo();
        private  bool _verifyLink;
        public UserInfo UserInfo
        {
            get { return _userInfo; }
            set { _userInfo = value; }
        }
       private List<IAlertable> _alertFlagObjs=new   List<IAlertable>();
        public string Message { get; set; } ="";
        public string? EmailTo { get { return _userInfo.Email; } }
        public string? UserID { get { return _userInfo.UserID; } }

        public string? Name { get { return _userInfo.Name; } }

        public string Subject { get; set; }="";

        public bool SendTrustPilot { get; set; }

        public bool dontSend { get; set; }
        public bool VerifyLink { get => _verifyLink; set => _verifyLink = value; }
        public List<IAlertable> AlertFlagObjs { get => _alertFlagObjs; set => _alertFlagObjs = value; }
    }
}