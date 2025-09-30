using System.Collections.Generic;
using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class MonitorIPHelperTests
{
    private static Mock<IProcessorState> BuildProcessorState()
    {
        var processors = new List<ProcessorObj>
        {
            new ProcessorObj { AppID = "agent-1", Location = "Scanner - EU", IsEnabled = true },
            new ProcessorObj { AppID = "agent-2", Location = "Scanner - US", IsEnabled = true }
        };

        var mock = new Mock<IProcessorState>();
        mock.Setup(m => m.EnabledProcessorList(false)).Returns(processors);
        return mock;
    }

    [Fact]
    public void ConvertLocationToAppID_FindsMatchCaseInsensitive()
    {
        var mockState = BuildProcessorState();

        var appId = MonitorIPHelper.ConvertLocationToAppID("scanner - eu", mockState.Object);

        Assert.Equal("agent-1", appId);
    }

    [Fact]
    public void ConvertLocationToAppID_ReturnsEmptyWhenUnknown()
    {
        var mockState = BuildProcessorState();

        var appId = MonitorIPHelper.ConvertLocationToAppID("unknown", mockState.Object);

        Assert.Equal(string.Empty, appId);
    }

    [Fact]
    public void ConvertAppIDToLocation_ReturnsLocation()
    {
        var mockState = BuildProcessorState();

        var location = MonitorIPHelper.ConvertAppIDToLocation("agent-2", mockState.Object);

        Assert.Equal("Scanner - US", location);
    }

    [Fact]
    public void ConvertAppIDToLocation_ReturnsNullWhenUnknown()
    {
        var mockState = BuildProcessorState();

        var location = MonitorIPHelper.ConvertAppIDToLocation("missing", mockState.Object);

        Assert.Null(location);
    }
}
