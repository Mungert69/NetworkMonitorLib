namespace NetworkMonitor.Objects;


public class ExceptionLog
{
    public int Id { get; set; }
    public string ExceptionType { get; set; }
    public string Message { get; set; }
    public string StackTrace { get; set; }
    public DateTime Timestamp { get; set; }
}

