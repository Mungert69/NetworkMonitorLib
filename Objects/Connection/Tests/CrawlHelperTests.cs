using System.Reflection;
using NetworkMonitor.Connection;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection;

public class CrawlHelperTests
{
    private static MethodInfo GetPrivateStaticMethod(string name)
    {
        var method = typeof(CrawlHelper).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    private static bool InvokePrivateBool(string methodName, string? value)
    {
        var method = GetPrivateStaticMethod(methodName);
        var result = method.Invoke(null, new object?[] { value });
        Assert.IsType<bool>(result);
        return (bool)result!;
    }

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/problem+json", true)]
    [InlineData("text/html", false)]
    [InlineData(null, false)]
    public void IsLikelyJsonContentType_DetectsExpectedValues(string? contentType, bool expected)
    {
        var actual = InvokePrivateBool("IsLikelyJsonContentType", contentType);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("application/xml", true)]
    [InlineData("application/rss+xml", true)]
    [InlineData("text/xml; charset=utf-8", true)]
    [InlineData("text/html", false)]
    [InlineData(null, false)]
    public void IsLikelyXmlContentType_DetectsExpectedValues(string? contentType, bool expected)
    {
        var actual = InvokePrivateBool("IsLikelyXmlContentType", contentType);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TryPrettyPrintJson_ReturnsIndentedJson()
    {
        var method = GetPrivateStaticMethod("TryPrettyPrintJson");
        var args = new object?[] { "{\"a\":1,\"b\":{\"c\":2}}", null };

        var successObj = method.Invoke(null, args);
        Assert.IsType<bool>(successObj);
        Assert.True((bool)successObj!);

        var pretty = Assert.IsType<string>(args[1]);
        Assert.Contains("\n", pretty);
        Assert.Contains("\"a\": 1", pretty);
    }

    [Fact]
    public void TryPrettyPrintXml_ReturnsIndentedXml()
    {
        var method = GetPrivateStaticMethod("TryPrettyPrintXml");
        var args = new object?[] { "<root><item>1</item><item>2</item></root>", null };

        var successObj = method.Invoke(null, args);
        Assert.IsType<bool>(successObj);
        Assert.True((bool)successObj!);

        var pretty = Assert.IsType<string>(args[1]);
        Assert.Contains("<root>", pretty);
        Assert.Contains("\n", pretty);
    }

    [Fact]
    public void TryPrettyPrintXml_RejectsHtmlDocument()
    {
        var method = GetPrivateStaticMethod("TryPrettyPrintXml");
        var args = new object?[] { "<html><body>hi</body></html>", null };

        var successObj = method.Invoke(null, args);
        Assert.IsType<bool>(successObj);
        Assert.False((bool)successObj!);
    }

    [Fact]
    public void NormalizeExtractedContent_RemovesJunkWhitespace()
    {
        var method = GetPrivateStaticMethod("NormalizeExtractedContent");
        var input = "   \n\nHello    world \n \n\nThis\t is   a   test\n\n";
        var result = method.Invoke(null, new object?[] { input });

        var normalized = Assert.IsType<string>(result);
        Assert.Equal("Hello world\n\nThis is a test", normalized);
    }
}
