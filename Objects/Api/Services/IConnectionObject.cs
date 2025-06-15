namespace NetworkMonitor.Objects
{
    public interface IConnectionObject
    {
        string Address { get; set; }
        ushort Port { get; set; }
        ushort Timeout { get; set; }
    }
}