using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace NetworkMonitor.Utils.Helpers;
public class GetConfigHelper
{
    public static string GetValueOrLogError(string key, string defaultValue, ILogger logger, IConfiguration config)
    {
        var value = config[key];
        if (value == null)
        {
            logger.LogError($"Missing configuration for '{key}'.");
            return defaultValue;
        }
        return value;
    }
}

