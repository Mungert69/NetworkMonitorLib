namespace NetworkMonitor.Objects.ServiceMessage;

/// <summary>
/// Requests that a processor reload its system or user authentication key.
/// </summary>
public sealed class RefreshAuthKeyObj
{
    public bool RefreshSystemAgent { get; set; }
    public bool RefreshUserAgent { get; set; }
    public string Reason { get; set; } = string.Empty;
}
