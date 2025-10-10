// File: NetworkMonitor.Objects/EndpointType.cs
namespace NetworkMonitor.Objects
{
    public class EndpointType
    {
        public string InternalType { get; set; } // e.g., "http", "icmp"
        public string Icon { get; set; }         // e.g., "HttpIcon"
        public string Name { get; set; }         // Friendly name
        public string Description { get; set; }  // Detailed description
        public string FunctionDescription { get; set; } // Description used in LLM function defs

        public EndpointType(string internalType, string icon, string name, string description, string? functionDescription = null)
        {
            InternalType = internalType;
            Icon = icon;
            Name = name;
            Description = description;
            FunctionDescription = functionDescription ?? description;
        }
    }
}
