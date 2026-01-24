using System.Collections.Generic;

namespace NetworkMonitor.Objects
{
    public class AgentFlowFileRequest
    {
        public string RequestId { get; set; } = "";
        public string UserId { get; set; } = "";
        public string FlowName { get; set; } = "";
        public string Action { get; set; } = "";
        public string? Json { get; set; }
        public bool Overwrite { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
