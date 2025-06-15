using System.Net.NetworkInformation;
namespace NetworkMonitor.Objects;
public class NetworkInterfaceInfo
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public NetworkInterfaceType Type { get; set; }
    public string? IPAddress { get; set; }
    public string? SubnetMask { get; set; }
    public int CIDR { get; set; }
    public bool IsPrivate { get; set; }
      public long NetworkSize { get; set; }

    public override string ToString()
    {
        return $"{Name} - {IPAddress}/{CIDR} ({Type})";
    }
}