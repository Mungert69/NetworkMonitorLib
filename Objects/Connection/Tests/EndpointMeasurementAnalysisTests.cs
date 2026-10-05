using NetworkMonitor.Connection;
using Xunit;
namespace NetworkMonitor.Connection.Tests;
public class EndpointMeasurementAnalysisTests
{
    [Theory]
    [InlineData("bthome","temperature","continuous","Celsius")]
    [InlineData("victron","battery_voltage","continuous","volts")]
    [InlineData("bthome","energy","counter","Accumulated")]
    [InlineData("bthome","opening_2","state","Binary")]
    [InlineData("bthome","button","event","event")]
    [InlineData("victron","device_state","code","Protocol")]
    [InlineData("ruuvi","measurement_sequence","sequence","sequence")]
    public void EncodingAndMeaningResolveTogether(string format,string metric,string kind,string description)
    {
        var metadata=EndpointMeasurementDefaults.Get("blebroadcast",$"--format {format} --metric {metric}");
        var encoding=BleMetricCatalogue.Find(format,metric)!;
        Assert.Equal(encoding.Unit,metadata.Unit);
        Assert.Equal(encoding.Scale,metadata.Scale);
        Assert.Equal(encoding.Offset,metadata.Offset);
        Assert.Equal(kind,metadata.AnalysisKind);
        Assert.Contains(description,metadata.Description);
        Assert.NotEmpty(metadata.AnalysisGuidance);
    }
    [Fact]
    public void MillisecondDurationUsesOriginalPerformanceAnalysisAndSensorsKeepTheirOwnGuidance()
    {
        var duration = EndpointMeasurementDefaults.Get("http");
        Assert.Contains("Identify noticeable spikes or periods of high response times", duration.AnalysisGuidance);
        Assert.Contains("Detect any timeouts", duration.AnalysisGuidance);
        Assert.Contains("optimize resource allocation", duration.AnalysisGuidance);
        Assert.InRange(duration.AnalysisGuidance.Length, 1, 2048);
        var partial = MeasurementAnalysisTemplates.Complete(new(AnalysisKind: "duration", TimingRatingThresholds: duration.TimingRatingThresholds));
        Assert.Equal(duration.AnalysisGuidance, partial.AnalysisGuidance);
        var voltage = EndpointMeasurementDefaults.Get("blebroadcast", "--format victron --metric battery_voltage");
        Assert.Contains("battery chemistry", voltage.AnalysisGuidance);
        Assert.DoesNotContain("high response times", voltage.AnalysisGuidance);
        var counter = EndpointMeasurementDefaults.Get("blebroadcast", "--format bthome --metric energy");
        Assert.Contains("Do not sum cumulative samples", counter.AnalysisGuidance);
    }

    [Fact]
    public void EverySupportedBleMetricHasBoundedCatalogueAnalysisMetadata()
    {
        foreach(var e in BleMetricCatalogue.Definitions) {
            var m=EndpointMeasurementDefaults.Get("blebroadcast",$"--format {e.Format} --metric {e.Metric}");
            Assert.NotEmpty(m.Description); Assert.NotEmpty(m.AnalysisKind); Assert.NotEmpty(m.AnalysisGuidance);
            Assert.InRange(m.Description.Length,1,512); Assert.InRange(m.AnalysisGuidance.Length,1,2048);
        }
    }
    [Fact]
    public void SharedConnectClassesKeepEndpointIdentityAndCompleteDefinitions()
    {
        var http = EndpointMeasurementDefaults.Get("http");
        var full = EndpointMeasurementDefaults.Get("httpfull");
        Assert.Equal("duration", http.AnalysisKind);
        Assert.Equal("duration", full.AnalysisKind);
        Assert.NotEqual(http.Description, full.Description);
        Assert.Contains("HttpFull", full.Description);
    }

    [Fact]
    public void PrimaryDefinitionIncludesEncodingAndMeaningWithConservativeFallback()
    {
        var explicitConnect = new DefinedConnect();
        var definition = Assert.Single(EndpointMeasurementDefinitionBuilder.Describe("custom", explicitConnect));
        Assert.Equal("°C", definition.Unit);
        Assert.Equal(-40, definition.Offset);
        Assert.Equal("Sensor temperature", definition.Description);
        var unspecified = Assert.Single(EndpointMeasurementDefinitionBuilder.Describe("unspecified", new UnspecifiedConnect()));
        Assert.Equal(-10, unspecified.Offset);
        Assert.Equal("unspecified", unspecified.AnalysisKind);
        Assert.DoesNotContain("battery", unspecified.AnalysisGuidance);
    }

    private sealed class DefinedConnect : NetConnect
    {
        public override EndpointMeasurementMetadata Measurement => new("°C", .1, Offset: -40,
            Description: "Sensor temperature", AnalysisKind: "continuous", AnalysisGuidance: "Assess measured temperature.");
        public override Task Connect() => throw new InvalidOperationException();
    }
    private sealed class UnspecifiedConnect : NetConnect
    {
        public override EndpointMeasurementMetadata Measurement => new("V", Offset: -10);
        public override Task Connect() => throw new InvalidOperationException();
    }
}
