using System.Collections.Generic;

namespace NetworkMonitor.Objects
{
    public class AgentFlowFileResponse
    {
        public string RequestId { get; set; } = "";
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? Json { get; set; }
        public List<string>? Flows { get; set; }
    }
}
