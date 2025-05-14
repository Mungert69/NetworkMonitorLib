using System.Text.Json.Serialization;
namespace NetworkMonitor.Objects
{
    
    public class PingParams

    {
        public PingParams(){

        }
        private int _timeOut;
        private int _alertThreshold;
        private int _hostLimit;
        public int Timeout { get => _timeOut; set => _timeOut = value; }
        public int AlertThreshold { get => _alertThreshold; set => _alertThreshold = value; }
        public int HostLimit { get => _hostLimit; set => _hostLimit = value; }
    }
}
