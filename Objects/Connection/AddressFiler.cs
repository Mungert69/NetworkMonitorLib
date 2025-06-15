using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
  public class AddressFilter
{
    // Method to call AddHttpsPrefix if EndPointType is http. Else call RemoveHttpsPrefix
    public static string FilterAddress(string address, string? type)
    {
        if (type == "http" || type=="httpfull" || type=="httphtml")
        {
            return AddHttpsPrefix(address);
        }
        else
        {
            return RemoveHttpsPrefix(address);
        }
    }
    private static string AddHttpsPrefix(string address)
    {

        if (!address.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + address;
        }

        return address;
    }

    private static string RemoveHttpsPrefix(string address)
    {

        if (address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return address.Substring("https://".Length);
        }

        if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return address.Substring("http://".Length);
        }

        return address;
    }
}

}