using Xunit;
namespace NetworkMonitor.Connection.Tests;
public class TimingMeasurementRatingTests
{
    [Theory]
    [InlineData("icmp", true)] [InlineData("http", true)] [InlineData("https", true)]
    [InlineData("httphtml", true)] [InlineData("httpfull", true)] [InlineData("dns", true)]
    [InlineData("smtp", true)] [InlineData("rawconnect", true)]
    [InlineData("nmap", false)] [InlineData("nmapvuln", false)] [InlineData("crawlsite", false)]
    [InlineData("dailycrawl", false)] [InlineData("dailyhugkeepalive", false)] [InlineData("hugwake", false)]
    [InlineData("configintegrity", false)] [InlineData("sitehash", false)]
    [InlineData("quantum", false)] [InlineData("quantumcert", false)]
    [InlineData("blebroadcast", false)] [InlineData("blebroadcastlisten", false)]
    public void OnlyExplicitTimingMeasurementsAreRated(string endpoint, bool enabled)
    {
        var definition = EndpointMeasurementDefaults.Get(endpoint);
        Assert.Equal(enabled, TimingMeasurementRating.IsEnabled(definition));
        if (!enabled) {
            Assert.Null(definition.TimingRatingThresholds);
            Assert.Null(TimingMeasurementRating.Category(1, definition));
            if (definition.AnalysisKind == "duration") Assert.Contains("no configured timing ratings", definition.AnalysisGuidance);
        }
    }
    [Theory]
    [InlineData(149d, "excellent")] [InlineData(150d, "good")] [InlineData(300d, "fair")]
    [InlineData(500d, "poor")] [InlineData(null, "bad")]
    public void RatedBoundariesRetainOriginalMeaning(double? value, string category)
        => Assert.Equal(category, TimingMeasurementRating.Category(value, EndpointMeasurementDefaults.Get("http")));
    [Fact]
    public void InvalidOrNonTimingThresholdsDoNotEnableRatings()
    {
        var duration = MeasurementAnalysisTemplates.Duration("operation", new(0, 0, 0));
        Assert.False(TimingMeasurementRating.IsEnabled(duration));
        Assert.False(new TimingRatingThresholds(100, 50, 200).IsValid());
        Assert.False(new TimingRatingThresholds(1, 2, double.NaN).IsValid());
        var voltage = MeasurementAnalysisTemplates.Metric("Voltage", "V") with { TimingRatingThresholds = new(1, 2, 3) };
        Assert.False(TimingMeasurementRating.IsEnabled(voltage));
        Assert.Throws<InvalidOperationException>(() => EndpointMeasurementDefinitionBuilder.Describe("invalid", new InvalidConnect()));
    }
    private sealed class InvalidConnect : NetConnect {
        public override EndpointMeasurementMetadata Measurement => MeasurementAnalysisTemplates.Metric("Voltage", "V") with { TimingRatingThresholds = new(1, 2, 3) };
        public override Task Connect() => Task.CompletedTask;
    }
}
