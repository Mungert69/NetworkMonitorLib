using NetworkMonitor.Objects;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects;

public class MemoryQueryRequestTests
{
    [Fact]
    public void Defaults_IncludeCurrentSessionIdAndExpectedValues()
    {
        var request = new MemoryQueryRequest();

        Assert.Equal(string.Empty, request.QueryText);
        Assert.Equal(string.Empty, request.UserId);
        Assert.Equal(string.Empty, request.SessionId);
        Assert.Equal(string.Empty, request.CurrentSessionId);
        Assert.Equal(8, request.TopK);
        Assert.False(request.IncludeToolTurns);
    }
}
