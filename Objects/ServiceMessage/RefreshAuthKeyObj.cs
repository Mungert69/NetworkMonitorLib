namespace NetworkMonitor.Objects.ServiceMessage
{
    public class RefreshAuthKeyObj
    {
        public bool RefreshUserAgent { get; set; }
        public bool RefreshSystemAgent { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
