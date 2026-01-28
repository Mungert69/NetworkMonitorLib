namespace NetworkMonitor.Objects;

public class RemoteCacheConfig
{
    public bool Enabled { get; set; } = false;
    public string Type { get; set; } = "Http";
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryAttempts { get; set; } = 3;
}