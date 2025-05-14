
namespace NetworkMonitor.Objects
{
    public class AlertFlagObj{
        public  AlertFlagObj(){}
        private int _iD;
        private string _appID="";

        public int ID { get => _iD; set => _iD = value; }
        public string AppID { get => _appID; set => _appID = value; }
    }
}