using NetworkMonitor.Connection;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection;

public class EndpointMeasurementDefaultsTests
{
    [Fact]
    public void Built_in_definitions_come_from_classes_and_unknown_endpoints_use_defaults()
    {
        Assert.Equal("raw value", EndpointMeasurementDefaults.Get("BLEBROADCAST").Unit);
        Assert.Equal("unspecified", EndpointMeasurementDefaults.Get("BLEBROADCAST").AnalysisKind);
        Assert.Equal("discovery", EndpointMeasurementDefaults.Get("blebroadcastlisten").AnalysisKind);
        Assert.Equal("duration", EndpointMeasurementDefaults.Get("http").AnalysisKind);
        Assert.Equal(1, EndpointMeasurementDefaults.Get("http").Scale);
        Assert.Equal("ms", EndpointMeasurementDefaults.Get("custom-unknown").Unit);
        Assert.NotEmpty(EndpointMeasurementDefaults.Get(null).AnalysisGuidance);
    }

    [Fact]
    public void Builder_reads_definitions_without_running_probes_and_disposes_connect_tokens()
    {
        var ordinary = new TestConnect();
        var voltage = new VoltageConnect();
        var definitions = EndpointMeasurementDefinitionBuilder.Build(new[] { "ordinary", "voltage" },
            endpoint => endpoint == "ordinary" ? ordinary : voltage);
        Assert.Equal(2, definitions.Count);
        var metadata = definitions.Single(d => d.Key == "voltage");
        var definition = Assert.Single(metadata.Value);
        Assert.Equal("V", definition.Unit);
        Assert.Equal(0.01, definition.Scale);
        Assert.Contains("volts", definition.Description);
        Assert.Equal("Do not assume a battery is connected.", definition.AnalysisGuidance);
        Assert.Same(metadata.Value, definitions["VOLTAGE"]);
        Assert.Throws<ObjectDisposedException>(() => ordinary.Cts.Token);
        Assert.Throws<ObjectDisposedException>(() => voltage.Cts.Token);
    }

    [Fact]
    public void DefaultDurationWithExplicitAnalysisMetadataIsPublished()
    {
        var result=EndpointMeasurementDefinitionBuilder.Build(new[]{"custom-duration"},_ => new DescribedDurationConnect());
        var metadata=Assert.Single(result["custom-duration"]);
        Assert.Equal("duration",metadata.AnalysisKind);
        Assert.Equal("Custom transaction completion time.",metadata.Description);
    }
    private sealed class DescribedDurationConnect : TestConnect
    {
        public override EndpointMeasurementMetadata Measurement => new(
            Description: "Custom transaction completion time.", AnalysisKind: "duration",
            AnalysisGuidance: "Describe transaction duration and availability.");
    }

    private class TestConnect : NetConnect
    {
        public override Task Connect() => throw new InvalidOperationException("Must not execute a probe");
    }

    private sealed class VoltageConnect : TestConnect
    {
        public override EndpointMeasurementMetadata Measurement => new("V", .01,
            Description: "Enclosure supply voltage in volts.", AnalysisKind: "continuous",
            AnalysisGuidance: "Do not assume a battery is connected.");
    }
}
