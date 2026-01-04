using System.Collections.Generic;
using System.Text.Json;
using NetworkMonitor.Utils;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class JsonUtilsTests
{
    [Fact]
    public void GetValueOrCoerce_Ushort_FromString()
    {
        var dict = new Dictionary<string, object>
        {
            ["port"] = "443"
        };

        var value = JsonUtils.GetValueOrCoerce<ushort?>(dict, "port");

        Assert.Equal((ushort)443, value);
    }

    [Fact]
    public void GetValueOrCoerce_Ushort_FromJsonElement()
    {
        using var doc = JsonDocument.Parse("{\"port\": 8443}");
        var dict = new Dictionary<string, object>
        {
            ["port"] = doc.RootElement.GetProperty("port")
        };

        var value = JsonUtils.GetValueOrCoerce<ushort?>(dict, "port");

        Assert.Equal((ushort)8443, value);
    }
}
