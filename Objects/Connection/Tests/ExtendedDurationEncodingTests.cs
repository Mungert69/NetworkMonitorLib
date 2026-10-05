using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.DTOs;
using Xunit;

namespace NetworkMonitor.Connection.Tests;

public class ExtendedDurationEncodingTests
{
    [Theory]
    [InlineData("nmap", 10)] [InlineData("nmapvuln", 10)]
    [InlineData("crawlsite", 20)] [InlineData("dailycrawl", 20)]
    [InlineData("dailyhugkeepalive", 20)] [InlineData("hugwake", 20)]
    [InlineData("blebroadcastlisten", 10)]
    public void CatalogueConvertsLongDurationSamplesToMilliseconds(string endpoint, int scale)
    {
        var metadata = EndpointMeasurementDefaults.Get(endpoint);
        Assert.Equal("ms", metadata.Unit);
        Assert.Equal(scale, metadata.Scale);
        Assert.Null(metadata.TimingRatingThresholds);
        var result = PhysicalMeasurementResponse.From(new HostResponseObj {
            EndPointType = endpoint, Unit = metadata.Unit, Scale = metadata.Scale, Offset = metadata.Offset,
            PacketsRecieved = 1, RoundTripTimeAverage = 7000,
            PingInfosDTO = new() { new() { ResponseTime = 7000, Status = "Complete", DateSent = DateTime.UtcNow } }
        });
        Assert.Equal(7000 * scale, result.Average);
        Assert.Equal(7000 * scale, Assert.Single(result.Readings).Value);
    }

    [Fact]
    public async Task NmapRecordsScaledDurationAndKeepsItsTimeoutExtension()
    {
        var processor = new Mock<ICmdProcessor>();
        processor.Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
            .Returns(async () => {
                await Task.Delay(70);
                return new ResultObj { Success = true, Message = "Nmap scan report for example.com (1.2.3.4)\nHost is up (0.10s latency).\n80/tcp open http" };
            });
        var provider = new Mock<ICmdProcessorProvider>();
        provider.Setup(p => p.GetProcessor("Nmap")).Returns(processor.Object);
        var connect = new TimedNmap(provider.Object) {
            MpiStatic = new MPIStatic { Address = "example.com", EndPointType = "nmap", Timeout = 59000 }
        };
        Assert.Equal(connect.Measurement.Scale, connect.Extension);
        await connect.Connect();
        Assert.True(connect.MpiConnect.IsUp);
        double decoded = connect.MpiConnect.PingInfo.RoundTripTime!.Value * connect.Measurement.Scale;
        Assert.InRange(connect.Elapsed - decoded, 0, connect.Measurement.Scale - 0.001);
    }

    private sealed class TimedNmap(ICmdProcessorProvider provider) : NmapCmdConnect(provider, "-sV")
    {
        public long Elapsed => Timer.ElapsedMilliseconds;
        public int Extension => ExtendTimeoutMultiplier;
    }
}
