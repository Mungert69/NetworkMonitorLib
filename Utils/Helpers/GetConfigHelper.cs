using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using DotNetEnv;
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


    public static string GetEnv(string key, string defaultValue = "")
    {
        string value = defaultValue;
        var envVar = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrEmpty(envVar)) value = envVar;

        return value;
    }




    public static string GetConfigValue(IConfiguration config, string key, string defaultValue = "")
    {
        var value = config.GetValue<string>(key) ?? defaultValue;
        if (value == ".env")
        {
            var envVar = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(envVar))
            {
                value = defaultValue;
            }
            else
            {
                value = envVar;
            }
            return value;
        }

        return value;
    }
    public static string GetConfigValue(ILogger logger, IConfiguration config, string key, string defaultValue = "")
    {
        var value = config.GetValue<string>(key) ?? defaultValue;
        if (value == ".env")
        {
            var envVar = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(envVar))
            {
                logger.LogError($"Environment variable '{key}' is not set. setting default value.");
                value = defaultValue;
            }
            else
            {
                logger.LogInformation($"Environment variable '{key}' found.");
                value = envVar;
            }
            return value;
        }
        else
        {
            logger.LogInformation($"Configuration key '{key}' Not changed from value in appsettings.");
        }
        return value;
    }
}

