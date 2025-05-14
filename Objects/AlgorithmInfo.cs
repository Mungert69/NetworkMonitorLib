namespace NetworkMonitor.Objects
{
    public class AlgorithmInfo
    {
        public AlgorithmInfo() { }

        public string AlgorithmName { get; set; } = "";
        public int DefaultID { get; set; }
        public bool Enabled { get; set; }
        public string EnvironmentVariable { get; set; } = "";
        public bool AddEnv { get; set; }

        // Additional fields for quantum algorithm information
        public string Description { get; set; } = "";
        public int KeySize { get; set; }
        public string SecurityLevel { get; set; } = "";
    }
}