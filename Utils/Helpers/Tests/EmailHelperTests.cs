using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class EmailHelperTests
{
    [Fact]
    public void NormalizeEmail_RemovesPlusSuffix()
    {
        var (isUpdated, email) = EmailHelper.NormalizeEmail("user+tag@example.com");

        Assert.True(isUpdated);
        Assert.Equal("user@example.com", email);
    }

    [Fact]
    public void NormalizeEmail_ReturnsOriginalWhenNoPlus()
    {
        var (isUpdated, email) = EmailHelper.NormalizeEmail("user@example.com");

        Assert.False(isUpdated);
        Assert.Equal("user@example.com", email);
    }

    [Fact]
    public void NormalizeEmail_PreservesNull()
    {
        var (isUpdated, email) = EmailHelper.NormalizeEmail(null);

        Assert.False(isUpdated);
        Assert.Null(email);
    }
}
