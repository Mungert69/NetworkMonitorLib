using NetworkMonitor.Connection;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class DeviceContextHelperTests
{
    [Fact]
    public void BuildMonitorLocation_PreservesConfiguredAgentNameOverGeographicLocation()
    {
        var context = new DeviceContext
        {
            NearestTown = "Bath",
            Country = "United Kingdom"
        };

        var location = DeviceContextHelper.BuildMonitorLocation(context, "user@example.com-QSAX-localhost");

        Assert.Equal("user@example.com-QSAX-localhost", location);
    }

    [Fact]
    public void BuildLlmDeviceContextSummary_UsesAgentNameAndIncludesGeographicContextSeparately()
    {
        var context = new DeviceContext
        {
            Hostname = "localhost",
            Platform = "Windows",
            NearestTown = "Bath",
            Country = "United Kingdom"
        };

        var summary = DeviceContextHelper.BuildLlmDeviceContextSummary(context, "user@example.com-QSAX-localhost");

        Assert.Contains("location=user@example.com-QSAX-localhost", summary);
        Assert.Contains("geographic_location=Bath United Kingdom", summary);
    }
}
