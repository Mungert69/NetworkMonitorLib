using System.Text.RegularExpressions;
using System.Text;
using NetworkMonitor.Objects.Repository;
namespace NetworkMonitor.Utils.Helpers;

public class MonitorIPHelper
{
   
    public static string ConvertLocationToAppID(string location, IProcessorState processorState)
    {
        string appID = "";
        //location = FormatEmailString(location.ToLower());
        var processorObj = processorState.EnabledProcessorList(false).Where(w => w.Location.ToLower() == location.ToLower()).FirstOrDefault();
        if (processorObj != null) appID = processorObj.AppID;

        return appID;
    }
    public static string? ConvertAppIDToLocation(string? appID, IProcessorState processorState)
    {
        string? location = null;
        if (appID == null) return location;
        var processorObj = processorState.EnabledProcessorList(false).Where(w => w.AppID == appID).FirstOrDefault();
        if (processorObj != null) location = processorObj.Location;
        return location;
    }
}
