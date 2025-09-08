namespace NetworkMonitor.Objects
{
    public interface IReadinessState
    {
        bool IsReady { get; set; }
        string? Reason { get; set; }
    }

    public sealed class ReadinessState : IReadinessState
    {
        public bool IsReady { get; set; }
        public string? Reason { get; set; }
    }
}
