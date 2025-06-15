

namespace NetworkMonitor.Objects.ServiceMessage
{
    
    public class ProcessorConnectObj
    
    {
        public ProcessorConnectObj(){

        }
        private int _nextRunInterval;
        private int _maxBuffer=2000;
        private string _authKey="";

        public int NextRunInterval { get => _nextRunInterval; set => _nextRunInterval = value; }
        public int MaxBuffer { get => _maxBuffer; set => _maxBuffer = value; }
        public string AuthKey { get => _authKey; set => _authKey = value; }
    }
}