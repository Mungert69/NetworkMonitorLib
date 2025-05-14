using System;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Linq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Connection;
namespace NetworkMonitor.Utils;

public class NetworkUtils
{
 public static int WordToPort(string word, int minValue = 1024, int maxValue = 65535)
    {
        if (minValue >= maxValue)
            throw new ArgumentException("minValue must be less than maxValue");

        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(word));
            int hashValue = BitConverter.ToInt32(hashBytes, 0); // Convert first 4 bytes to int

            // Map hash to range [minValue, maxValue]
            return Math.Abs(hashValue) % (maxValue - minValue + 1) + minValue;
        }
    }
    public static int SubnetMaskToCIDR(string subnetMask)
    {
        var maskBytes = IPAddress.Parse(subnetMask).GetAddressBytes();
        int cidr = 0;
        foreach (var b in maskBytes)
        {
            cidr += Convert.ToString(b, 2).Count(c => c == '1');
        }
        return cidr;
    }

    public static (string, string, string) GetLocalIPAddressAndSubnetMaskOld(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates)
    {
        var message = "Searching for appropriate network interface...\n";
        logger.LogInformation(message);
        cmdProcessorStates.RunningMessage += message;

        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            message = $"Checking interface: {ni.Name}, Type: {ni.NetworkInterfaceType}, OperationalStatus: {ni.OperationalStatus}\n";
            logger.LogInformation(message);
            cmdProcessorStates.RunningMessage += message;
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                ni.Description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                message = "Skipping this interface.\n";
                logger.LogInformation(message);
                cmdProcessorStates.RunningMessage += message;
                continue;
            }

            if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
            {
                message = "Not an Ethernet or Wi-Fi interface, will use only if no better option found.\n";
                logger.LogInformation(message);
                cmdProcessorStates.RunningMessage += message;
                continue;
            }

            var ipProperties = ni.GetIPProperties();
#if WINDOWS
            // Check for default gateway
            if (!ipProperties.GatewayAddresses.Any())
            {
                message= "No default gateway found, skipping.\n";
                 logger.LogInformation(message);
        cmdProcessorStates.RunningMessage += message;
                continue;
            }
#endif

            foreach (UnicastIPAddressInformation ip in ipProperties.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip.Address))
                {
                    int cidr = SubnetMaskToCIDR(ip.IPv4Mask.ToString());
                    message = $"Selected IP: {ip.Address}, Subnet Mask: {ip.IPv4Mask}, CIDR: {cidr}\n";

                    logger.LogInformation(message);
                    cmdProcessorStates.RunningMessage += message;
                    return (ip.Address.ToString(), ip.IPv4Mask.ToString(), cidr.ToString());
                }
            }
        }

        throw new Exception("No suitable local IP Address and Subnet Mask found!\n");

    }
    public static (string, string, string) GetLocalIPAddressAndSubnetMask(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates)
    {
        var message = "Searching for appropriate network interface...\n";
        logger.LogInformation(message);
        cmdProcessorStates.RunningMessage += message;

        NetworkInterface? selectedInterface = null;
        IPAddress? selectedIpAddress = null;
        UnicastIPAddressInformation? selectedIpInfo = null;

        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            message = $"Checking interface: {ni.Name}, Type: {ni.NetworkInterfaceType}, OperationalStatus: {ni.OperationalStatus}\n";
            logger.LogInformation(message);
            cmdProcessorStates.RunningMessage += message;
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                ni.Description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                message = "Skipping this interface.\n";
                logger.LogInformation(message);
                cmdProcessorStates.RunningMessage += message;
                continue;
            }

            var ipProperties = ni.GetIPProperties();

            foreach (UnicastIPAddressInformation ip in ipProperties.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip.Address))
                {
                    // Check if the IP address is within a private range
                    if (IsPrivateIpAddress(ip.Address))
                    {
                        int cidr = SubnetMaskToCIDR(ip.IPv4Mask.ToString());
                        message = $"Selected Private IP: {ip.Address}, Subnet Mask: {ip.IPv4Mask}, CIDR: {cidr}\n";
                        logger.LogInformation(message);
                        cmdProcessorStates.RunningMessage += message;
                        return (ip.Address.ToString(), ip.IPv4Mask.ToString(), cidr.ToString());
                    }
                    // Fall back to this interface if no private IPs are found later
                    if (selectedIpAddress == null)
                    {
                        selectedInterface = ni;
                        selectedIpAddress = ip.Address;
                        selectedIpInfo = ip;
                    }
                }
            }
        }

        if (selectedIpAddress != null && selectedIpInfo != null)
        {
            int cidr = SubnetMaskToCIDR(selectedIpInfo.IPv4Mask.ToString());
            message = $"Fallback IP: {selectedIpAddress}, Subnet Mask: {selectedIpInfo.IPv4Mask}, CIDR: {cidr}\n";
            logger.LogInformation(message);
            cmdProcessorStates.RunningMessage += message;
            return (selectedIpAddress.ToString(), selectedIpInfo.IPv4Mask.ToString(), cidr.ToString());
        }

        throw new Exception("No suitable local IP Address and Subnet Mask found!\n");
    }

    public static bool IsPrivateIpAddress(IPAddress ipAddress)
    {
        byte[] bytes = ipAddress.GetAddressBytes();
        switch (bytes[0])
        {
            case 10:
                return true; // 10.0.0.0 to 10.255.255.255
            case 172:
                return bytes[1] >= 16 && bytes[1] <= 31; // 172.16.0.0 to 172.31.255.255
            case 192:
                return bytes[1] == 168; // 192.168.0.0 to 192.168.255.255
            default:
                return false;
        }
    }

    public static (string, string, string) GetLocalIPAddressAndSubnetMaskFromInterfaceName(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates)
    {
        var message = "Searching for appropriate network interface...\n";
        logger.LogInformation(message);
        cmdProcessorStates.RunningMessage += message;

        NetworkInterface? selectedInterface = null;

        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            message = $"Checking interface: {ni.Name}, Type: {ni.NetworkInterfaceType}, OperationalStatus: {ni.OperationalStatus}\n";
            logger.LogInformation(message);
            cmdProcessorStates.RunningMessage += message;
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                ni.Description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                message = "Skipping this interface.\n";
                logger.LogInformation(message);
                cmdProcessorStates.RunningMessage += message;
                continue;
            }

            // Prefer Ethernet or Wi-Fi over other types
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            {
                selectedInterface = ni;
                message = $"Using prefered Ethernet or Wifi interface {ni.NetworkInterfaceType}.\n";
                logger.LogInformation(message);
                cmdProcessorStates.RunningMessage += message;
                break; // Immediate selection for these types
            }

            // If no better interface is found, fall back to others
            if (selectedInterface == null)
            {
                message = $"Found non prefered interface {ni.NetworkInterfaceType}.\n";
                logger.LogInformation(message);
                cmdProcessorStates.RunningMessage += message;
                selectedInterface = ni;
            }
        }

        if (selectedInterface != null)
        {
            var ipProperties = selectedInterface.GetIPProperties();

            foreach (UnicastIPAddressInformation ip in ipProperties.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip.Address))
                {
                    int cidr = SubnetMaskToCIDR(ip.IPv4Mask.ToString());
                    message = $"Selected IP: {ip.Address}, Subnet Mask: {ip.IPv4Mask}, CIDR: {cidr}\n";
                    logger.LogInformation(message);
                    cmdProcessorStates.RunningMessage += message;
                    return (ip.Address.ToString(), ip.IPv4Mask.ToString(), cidr.ToString());
                }
            }
        }

        throw new Exception("No suitable local IP Address and Subnet Mask found!\n");
    }
    public static List<NetworkInterfaceInfo> GetSuitableNetworkInterfaces(ILogger logger, ILocalCmdProcessorStates cmdProcessorStates)
    {
        var message = "Searching for suitable network interfaces...\n";
        logger.LogInformation(message);
        cmdProcessorStates.RunningMessage += message;

        var suitableInterfaces = new List<NetworkInterfaceInfo>();

        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            message = $"Checking interface: {ni.Name}, Type: {ni.NetworkInterfaceType}, OperationalStatus: {ni.OperationalStatus}\n";
            logger.LogInformation(message);
            cmdProcessorStates.RunningMessage += message;

            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                ni.Description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var ipProperties = ni.GetIPProperties();
            foreach (UnicastIPAddressInformation ip in ipProperties.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip.Address))
                {
                    int cidr = SubnetMaskToCIDR(ip.IPv4Mask.ToString());
                     long networkSize = (long)Math.Pow(2, 32 - cidr);
               
                    suitableInterfaces.Add(new NetworkInterfaceInfo
                    {
                        Name = ni.Name,
                        Description = ni.Description,
                        Type = ni.NetworkInterfaceType,
                        IPAddress = ip.Address.ToString(),
                        SubnetMask = ip.IPv4Mask.ToString(),
                        CIDR = cidr,
                        IsPrivate = IsPrivateIpAddress(ip.Address),
                        NetworkSize=networkSize
                    });
                }
            }
        }

        // 1. Private IPs
    // 2. Ethernet and Wi-Fi
    // 3. Interface name
    suitableInterfaces = suitableInterfaces
        .OrderByDescending(i => i.IsPrivate)
        .ThenBy(i => i.NetworkSize)
        .ThenBy(i => i.Type != NetworkInterfaceType.Ethernet && 
                     i.Type != NetworkInterfaceType.Wireless80211 ? 1 : 0)
        .ThenBy(i => i.Name)
        .ToList();

        return suitableInterfaces;
    }
    public static (int networkAddress, int startIP, int endIP) GetNetworkRange(string ipAddress, string subnetMask)
    {
        int ipInt = IpToInt(ipAddress);
        int maskInt = IpToInt(subnetMask);

        int networkAddress = ipInt & maskInt;
        int broadcastAddress = networkAddress | ~maskInt;

        int startIP = 1; // Network address + 1
        int endIP = broadcastAddress - networkAddress - 1; // Broadcast address - network address - 1

        return (networkAddress, startIP, endIP);
    }

    public static int IpToInt(string ipAddress)
    {
        return BitConverter.ToInt32(IPAddress.Parse(ipAddress).GetAddressBytes().Reverse().ToArray(), 0);
    }

    public static string IntToIp(int ipInt)
    {
        return new IPAddress(BitConverter.GetBytes(ipInt).Reverse().ToArray()).ToString();
    }

}
