using NetworkMonitor.Connection;
using Xunit;

public class AddressFilterTest
{
    [Theory]
    [InlineData("example.com", "http", "https://example.com")]
    [InlineData("http://example.com", "http", "http://example.com")]
    [InlineData("https://example.com", "http", "https://example.com")]
    [InlineData("example.com", "httpfull", "https://example.com")]
    [InlineData("example.com", "httphtml", "https://example.com")]
    [InlineData("https://example.com", "smtp", "example.com")]
    [InlineData("http://example.com", "smtp", "example.com")]
    [InlineData("example.com", "smtp", "example.com")]
    [InlineData("https://example.com", null, "example.com")]
    [InlineData("http://example.com", null, "example.com")]
    [InlineData("example.com", null, "example.com")]
    public void FilterAddress_WorksAsExpected(string address, string type, string expected)
    {
        var result = AddressFilter.FilterAddress(address, type);
        Assert.Equal(expected, result);
    }
}
