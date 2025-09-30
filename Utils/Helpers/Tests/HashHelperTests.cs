using System.Linq;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class HashHelperTests
{
    [Fact]
    public void ComputeSha256Hash_ReturnsExpectedValue()
    {
        var hash = HashHelper.ComputeSha256Hash("hello");

        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash);
    }

    [Fact]
    public void ComputeSha3_256Hash_ProducesHexString()
    {
        var hash = HashHelper.ComputeSha3_256Hash("hello");

        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(char.IsLetterOrDigit));
    }
}
