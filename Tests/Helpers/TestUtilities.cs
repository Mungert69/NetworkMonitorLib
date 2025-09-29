using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NetworkMonitor.Utils.Helpers;

namespace NetworkMonitorLib.Tests.Helpers;

internal static class TestUtilities
{
    internal static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal static void ResetGetConfigHelper()
    {
        var type = typeof(GetConfigHelper);
        var configField = type.GetField("_config", BindingFlags.Static | BindingFlags.NonPublic);
        var loggerField = type.GetField("_logger", BindingFlags.Static | BindingFlags.NonPublic);
        configField?.SetValue(null, null);
        loggerField?.SetValue(null, null);
    }

    internal static string CreateTempFile(string? content = "")
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, content ?? string.Empty);
        return path;
    }
}
