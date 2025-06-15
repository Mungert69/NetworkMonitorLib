namespace NetworkMonitor.Objects.Entity
{
    public class LLMSessionLog
    {
        public int ID { get; set; }
        public string SessionId { get; set; } = "";
        public string UserId { get; set; } = "";
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;

        // Navigation property
        public List<LLMSessionOutput> LLMSessionOutputs { get; set; } = new List<LLMSessionOutput>();
    }

    public class LLMSessionOutput
    {
        public int ID { get; set; }
        public string Output { get; set; } = "";
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;

        // Foreign key to LLMSessionLog
        public int LLMSessionLogId { get; set; }
        public LLMSessionLog LLMSessionLog { get; set; } = new LLMSessionLog();
    }
}
