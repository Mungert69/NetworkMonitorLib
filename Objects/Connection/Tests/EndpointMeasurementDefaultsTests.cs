using NetworkMonitor.Connection;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection;

public class EndpointMeasurementDefaultsTests
{
    [Fact]
    public void Built_in_definitions_come_from_classes_and_unknown_endpoints_use_defaults()
    {
        Assert.Equal(new EndpointMeasurementMetadata("raw value"), EndpointMeasurementDefaults.Get("BLEBROADCAST"));
        Assert.Equal(new EndpointMeasurementMetadata(), EndpointMeasurementDefaults.Get("blebroadcastlisten"));
        Assert.Equal(new EndpointMeasurementMetadata(), EndpointMeasurementDefaults.Get("http"));
        Assert.Equal(new EndpointMeasurementMetadata(), EndpointMeasurementDefaults.Get("custom-unknown"));
        Assert.Equal(new EndpointMeasurementMetadata(), EndpointMeasurementDefaults.Get(null));
    }

    [Fact]
    public void Builder_reads_properties_without_running_probes_and_disposes_connect_tokens()
    {
        var ordinary = new TestConnect();
        var voltage = new VoltageConnect();
        var definitions = EndpointMeasurementDefinitionBuilder.Build(new[] { "ordinary", "voltage" },
            endpoint => endpoint == "ordinary" ? ordinary : voltage);
        var metadata = Assert.Single(definitions);
        Assert.Equal(new EndpointMeasurementMetadata("V", 0.01), Assert.Single(metadata.Value));
        Assert.Same(metadata.Value, definitions["VOLTAGE"]);
        Assert.Throws<ObjectDisposedException>(() => ordinary.Cts.Token);
        Assert.Throws<ObjectDisposedException>(() => voltage.Cts.Token);
    }

    private class TestConnect : NetConnect
    {
        public override Task Connect() => throw new InvalidOperationException("Must not execute a probe");
    }

    private sealed class VoltageConnect : TestConnect
    {
        public override string Unit => "V";
        public override double Scale => 0.01;
    }
}
