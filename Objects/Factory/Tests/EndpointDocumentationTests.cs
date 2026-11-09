using System.Text.Json;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects.Factory;
using Xunit;

namespace NetworkMonitor.Objects.Tests;

public class EndpointDocumentationTests
{
    private static JsonElement Describe(string endpoint, string? format = null, string? record = null, string? metric = null)
        => JsonSerializer.SerializeToElement(EndpointDocumentation.Describe(endpoint, format, record, metric));

    [Fact]
    public void EndpointOverviewIsCompactAndCoversAllFormats()
    {
        var overview = Describe("blebroadcast");
        Assert.Equal(6, overview.GetProperty("formats").GetArrayLength());
        Assert.False(overview.TryGetProperty("metrics", out _));
        Assert.False(overview.TryGetProperty("records", out _));
        Assert.Contains("password", overview.GetProperty("description").GetString());
    }

    [Fact]
    public void SolarMetricsMatchDecoderAndExcludeOtherVictronRecords()
    {
        var result = Describe("blebroadcast", "victron", "solar_charger");
        var names = result.GetProperty("metrics").EnumerateArray().Select(m => m.GetProperty("metric").GetString()).ToArray();
        Assert.Equal(new VictronSolarChargerRecordDecoder().Metrics.Select(m => m.Metric).OrderBy(m => m), names);
        Assert.Contains("charger_error", names);
        Assert.DoesNotContain("state_of_charge", names);
        Assert.Throws<ArgumentException>(() => Describe("blebroadcast", "victron", "solar_charger", "state_of_charge"));
        Assert.Equal("counter", Describe("blebroadcast", "victron", "solar_charger", "yield_today").GetProperty("analysis_kind").GetString());
    }

    [Theory]
    [InlineData("ruuvi")]
    [InlineData("bthome")]
    public void ProtocolsHaveIndependentMetricsExamplesAndMeanings(string format)
    {
        var result = Describe("blebroadcast", format);
        Assert.Equal($"--format {format} --metric temperature", result.GetProperty("example").GetProperty("args").GetString());
        var metric = Describe("blebroadcast", format, metric: "temperature");
        Assert.Equal("°C", metric.GetProperty("unit").GetString());
        Assert.Equal(BleMetricCatalogue.Find(format, "temperature")!.Meaning.AnalysisGuidance,
            metric.GetProperty("analysis_guidance").GetString());
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("aesgcm")]
    [InlineData("aesctr")]
    public void GenericFormatsDoNotClaimPhysicalMetrics(string format)
    {
        Assert.False(Describe("blebroadcast", format).TryGetProperty("metrics", out _));
        Assert.Throws<ArgumentException>(() => Describe("blebroadcast", format, metric: "temperature"));
    }

    [Fact]
    public void ArgumentDocumentationUsesParserSchemasWithoutSharingMutableInstances()
    {
        var result = Describe("blebroadcast");
        foreach (var argument in BleCommandArguments.CreateBroadcast().Where(a => result.GetProperty("arguments").TryGetProperty(a.Key, out _)))
        {
            var documented = result.GetProperty("arguments").GetProperty(argument.Key);
            Assert.Equal(argument.Help, documented.GetProperty("description").GetString());
            Assert.Equal(argument.DefaultValue, documented.GetProperty("default").GetString());
        }
        var schema = BleCommandArguments.CreateBroadcast();
        schema[0].Help = "Changed by caller";
        Assert.NotEqual(schema[0].Help, BleCommandArguments.CreateBroadcast()[0].Help);
        var listen = Describe("blebroadcastlisten");
        Assert.Equal(BleCommandArguments.CreateListen().Single(a => a.Key == "max_captures").Help,
            listen.GetProperty("arguments").GetProperty("max_captures").GetProperty("description").GetString());
    }

    [Fact]
    public void NewProvidersCanBeComposedWithoutChangingDiscoveryRouter()
    {
        var catalogue = new EndpointDocumentationCatalogue(new[] {
            new BleBroadcastDocumentationProvider(new[] { new ExampleFormatProvider() })
        });
        var overview = catalogue.Describe(new("blebroadcast"));
        Assert.Equal(new[] { "example" }, overview.Formats);
        Assert.Equal("Custom format documentation", catalogue.Describe(new("blebroadcast", "example")).Description);
    }

    private sealed class ExampleFormatProvider : IBleFormatDocumentationProvider
    {
        public string Format => "example";
        public NetworkMonitor.DTOs.EndpointDocumentationResponse Describe(EndpointDocumentationQuery query) => new() {
            Endpoint = query.Endpoint, Format = Format, Description = "Custom format documentation"
        };
    }

    [Fact]
    public void InvalidFiltersAndListenDecodingAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Describe("blebroadcast", metric: "temperature"));
        Assert.Throws<ArgumentException>(() => Describe("blebroadcast", "unknown"));
        Assert.Throws<ArgumentException>(() => Describe("blebroadcast", "ruuvi", "solar_charger"));
        Assert.Throws<ArgumentException>(() => Describe("blebroadcastlisten", "victron"));
        Assert.Contains("Does not decrypt", Describe("blebroadcastlisten").GetProperty("description").GetString());
    }
}
