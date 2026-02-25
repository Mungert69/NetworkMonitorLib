using System;
using System.Text.Json;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.ServiceMessage;

public class ProcessorScanDataObjTests
{
    [Fact]
    public void RawData_SerializesAndDeserializes_AsBase64Payload()
    {
        var obj = new ProcessorScanDataObj
        {
            MessageID = "msg-1",
            RawData = new byte[] { 0, 1, 2, 3, 254, 255 },
            RawDataMimeType = "image/jpeg",
            RawDataEncoding = "base64",
            RawDataLength = 6,
            RawDataSha256 = "abc123"
        };

        var json = JsonSerializer.Serialize(obj);
        var roundTrip = JsonSerializer.Deserialize<ProcessorScanDataObj>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal(obj.RawData, roundTrip!.RawData);
        Assert.Equal(Convert.ToBase64String(obj.RawData), Convert.ToBase64String(roundTrip.RawData));
        Assert.Equal("image/jpeg", roundTrip.RawDataMimeType);
        Assert.Equal("base64", roundTrip.RawDataEncoding);
        Assert.Equal(6, roundTrip.RawDataLength);
        Assert.Equal("abc123", roundTrip.RawDataSha256);
    }
}
