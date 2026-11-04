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
        /// <summary>
        /// Whether this endpoint type produces a meaningful response-time (RoundTripTime)
        /// measurement that is suitable for TimesFM change/spike prediction analysis.
        /// Endpoint types whose measurement is not a duration (e.g. blebroadcastlisten,
        /// blebroadcast, configintegrity) should be set to false.
        /// </summary>
        public bool IsSuitableForPrediction { get; set; } = true;

        public EndpointType(string internalType, string icon, string name, string description, string? functionDescription = null, bool isSuitableForPrediction = true)
        {
            InternalType = internalType;
            Icon = icon;
            Name = name;
            Description = description;
            FunctionDescription = functionDescription ?? description;
            IsSuitableForPrediction = isSuitableForPrediction;
        }
    }
}
