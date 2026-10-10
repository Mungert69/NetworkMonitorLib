using System.Threading;
using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitor.Connection.Tests;

public class BleMetricTests
{
    [Fact]
    public void PublishedVersionTwoDefinitionsNeverChange()
    {
        using var stream = typeof(BleMetricCatalogue).Assembly.GetManifestResourceStream("BleMetricEncodingsV2");
        Assert.NotNull(stream);
        var frozen = System.Text.Json.JsonSerializer.Deserialize<BleMetricEncoding[]>(stream!)!;
        // Wording can evolve; every versioned numeric encoding must remain identical.
        static object Encoding(BleMetricEncoding e) => new { e.Format, e.Metric, e.Unit, e.Scale, e.Offset, e.Minimum, e.Maximum };
        Assert.Equal(frozen.OrderBy(e => e.Type).Select(Encoding), BleMetricCatalogue.Definitions.OrderBy(e => e.Type).Select(Encoding));
    }

    [Fact]
    public void AutomaticEncodingsCoverPublishedRangesWithoutFailureMarker()
    {
        foreach (var encoding in BleMetricCatalogue.Definitions)
        {
            Assert.True(encoding.Type.Length <= 50);
            foreach (var physical in new[] { encoding.Minimum, encoding.Maximum, (encoding.Minimum + encoding.Maximum) / 2 })
            {
                Assert.True(encoding.TryEncode(physical, out var sample), encoding.Type);
                Assert.InRange(sample, (ushort)0, (ushort)65534);
                Assert.InRange(Math.Abs(encoding.Decode(sample) - physical), 0, encoding.Scale / 2 + 1e-6);
            }
            Assert.False(encoding.TryEncode(double.NaN, out _));
            Assert.False(encoding.TryEncode(encoding.Maximum + encoding.Scale, out _));
            Assert.Equal(encoding, BleMetricCatalogue.FromStatus(encoding.Status));
        }
        Assert.Equal(BleMetricCatalogue.Find("bthome", "temperature")!.Scale,
            BleMetricCatalogue.Find("bthome", "temperature_2")!.Scale);
        Assert.NotNull(BleMetricCatalogue.Find("bthome", "mass_lb"));
        Assert.NotNull(BleMetricCatalogue.Find("bthome", "mass_kg"));
    }

    [Fact]
    public void EveryVictronLayoutExposesSelectableNumericFields()
    {
        foreach (var decoder in VictronDeviceRecordDecoders.CreateDefaults())
        {
            var readings = new List<BleReading>();
            Assert.True(decoder.TryAppend(new byte[16], new System.Text.StringBuilder(), readings, out var error), error);
            Assert.NotEmpty(readings);
            foreach (var reading in readings)
            {
                bool ok = BleMetricSelector.TrySelect(readings, reading.Metric, 1, 1000, out var value, out var label, out error);
                Assert.Equal(reading.Value.HasValue, ok);
                if (ok) Assert.Equal((ushort)Math.Round(reading.Value!.Value + 1000, MidpointRounding.AwayFromZero), value);
                Assert.Equal(reading.Metric, label);
            }
        }
    }

    [Theory]
    [InlineData("temperature", 2430)]
    [InlineData("humidity", 5349)]
    [InlineData("pressure", 10004)]
    [InlineData("battery_voltage", 298)]
    [InlineData("movement_counter", 66)]
    public void RuuviPublishedReadingsSelectWithDefaultScaling(string metric, int expected)
    {
        var d = new RuuviPayloadDecoder();
        Assert.True(d.TryDecodeReadings(new BlePayload("device", "raw_input", Convert.FromHexString("0512FC5394C37C0004FFFC040CAC364200CDCBB8334C884F")), Array.Empty<byte>(), out var decoded, out var error), error);
        Assert.True(BleMetricSelector.TrySelect(decoded.Readings, metric, null, 0, out var sample, out _, out error), error);
        Assert.Equal((ushort)expected, sample);
    }

    [Fact]
    public void BthomeRepeatedBinaryAndEventReadingsHaveDistinctNames()
    {
        var d = new BTHomePayloadDecoder();
        Assert.True(d.TryDecodeReadings(new BlePayload("device", "raw_input", Convert.FromHexString("4002C40902D0070F013A013A023C0203")), Array.Empty<byte>(), out var decoded, out var error), error);
        foreach (var (metric, expected) in new[] { ("temperature", 2500), ("temperature_2", 2000), ("boolean", 1), ("button", 1), ("button_2", 2), ("dimmer", 2), ("dimmer_steps", 3) })
        {
            Assert.True(BleMetricSelector.TrySelect(decoded.Readings, metric, null, 0, out var sample, out _, out error), error);
            Assert.Equal((ushort)expected, sample);
        }
    }

    [Fact]
    public void AuthenticatedBthomeReturnsValuesAndFailuresReturnNone()
    {
        var d = new BTHomePayloadDecoder();
        var capture = new BlePayload("54:48:E6:8F:80:A5", "raw_input", Convert.FromHexString("D2FC41E445F3C9962B332211006C7C4519"));
        byte[] key = Convert.FromHexString("231D39C1D7CC1AB1AEE224CD096DB932");
        Assert.True(d.TryDecodeReadings(capture, key, out var decoded, out var error), error);
        Assert.Equal(25.06, decoded.Readings.Single(r => r.Metric == "temperature").Value!.Value, 6);
        key[0] ^= 1;
        Assert.False(d.TryDecodeReadings(capture, key, out decoded, out _));
        Assert.Empty(decoded.Readings);
    }

    [Theory]
    [InlineData(-5.5, 100, 100, true, 9450)]
    [InlineData(-5.5, 100, 0, false, 0)]
    [InlineData(70000, 1, 0, false, 0)]
    [InlineData(70000, 0.1, 0, true, 7000)]
    [InlineData(1.005, 100, 0, true, 101)]
    public void ScaledSamplesUseOffsetsAndRejectOutOfRange(double number, double scale, double offset, bool success, int expected)
    {
        Assert.Equal(success, BleMetricSelector.TrySelect(new[] { new BleReading("reading", number, "") }, "reading", scale, offset, out var sample, out _, out _));
        Assert.Equal((ushort)expected, sample);
    }

    [Theory]
    [InlineData("temperature", null)]
    [InlineData("temperature", double.NaN)]
    [InlineData("temperature", double.PositiveInfinity)]
    [InlineData("absent", 25.0)]
    public void UnavailableAndNonnumericSelectionsFail(string metric, double? value)
    {
        Assert.False(BleMetricSelector.TrySelect(new[] { new BleReading("temperature", value, "°C", .01) }, metric, null, 0, out _, out _, out _));
    }

    [Theory]
    [InlineData("--format bthome --metric temperature", true, 2500)]
    [InlineData("--format bthome --metric=temperature --metric_scale=10 --metric_offset=100", true, 1250)]
    [InlineData("--format bthome --metric missing", false, 0)]
    [InlineData("--format bthome --metric temperature --metric_scale NaN", false, 0)]
    [InlineData("--format bthome --metric temperature --metric_scale 0", false, 0)]
    [InlineData("--format bthome --metric temperature --metric_offset -30", false, 0)]
    [InlineData("--format bthome --metric temperature --metric_scale 10000", false, 0)]
    [InlineData("--format bthome --metric", false, 0)]
    [InlineData("--format bthome --metric temperature --metric humidity", false, 0)]
    public async Task EndpointUsesStructuredValuesRatherThanDisplayText(string args, bool success, int expected)
    {
        var processor = new Mock<ICmdProcessor>();
        processor.Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
            .ReturnsAsync(new ResultObj { Success = true, Message = "Temperature: 999 °C", Data = new BleDecodedPayload("diagnostics", new[] { new BleReading("temperature", 25, "°C", .01) }) });
        var provider = new Mock<ICmdProcessorProvider>();
        provider.Setup(p => p.GetProcessor("BleBroadcast")).Returns(processor.Object);
        var connect = new BleBroadcastConnect(provider.Object) { MpiStatic = new MPIStatic { Address = "AA:BB:CC:DD:EE:FF", EndPointType = "blebroadcast", Args = args, Timeout = 2000 } };
        // Supply the coordinator's prerequisite while isolating structured-result selection.
        connect.PrepareSnapshot(new BleAdvertisementSnapshot(0, System.DateTime.UtcNow, 0,
            System.Array.Empty<BleAdvertisement>(), ""));
        await connect.Connect();
        Assert.Equal(success, connect.MpiConnect.IsUp);
        bool automatic = success && !args.Contains("metric_scale") && !args.Contains("metric_offset");
        Assert.Equal(success ? automatic ? "BLE v2:bthome:temperature" : "BLE temperature" : "BLE Metric Error", connect.MpiConnect.PingInfo.Status);
        if (automatic) {
            Assert.True(BleMetricCatalogue.Find("bthome", "temperature")!.TryEncode(25, out var encoded));
            expected = encoded;
        }
        if (success) Assert.Equal((ushort)expected, connect.MpiConnect.PingInfo.RoundTripTime);
    }
}
