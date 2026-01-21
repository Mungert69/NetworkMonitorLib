using System.Collections.Generic;

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ProcessorCustomConnectUpdateObj
    {
        public string AgentLocation { get; set; } = "";
        public string Mode { get; set; } = "";
        public string ConnectType { get; set; } = "";
        public List<string> ConnectTypes { get; set; } = new List<string>();
    }
}
