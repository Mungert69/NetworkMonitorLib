using System.Text.Json;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;
using Xunit;
namespace NetworkMonitor.Connection.Tests;

public class MeasurementAlertLimitsTests
{
    [Theory]
    [InlineData(null, null, 32757, null)]
    [InlineData(-1.2, -1.0, 32757, null)]
    [InlineData(-1.1, null, 32757, null)]
    [InlineData(null, -1.1, 32757, null)]
    [InlineData(-1.0, null, 32757, "low")]
    [InlineData(null, -1.2, 32757, "high")]
    [InlineData(-1.0, null, 65535, null)]
    [InlineData(2.0, 1.0, 32757, null)]
    public void ComparesPhysicalValuesAndExcludesFailureSamples(double? low, double? high, ushort sample, string? expected)
    {
        var metadata = MeasurementAnalysisTemplates.Metric("Battery current", "A") with { Scale = .1, Offset = -3276.8 };
        // Floating-point equality is tested with an exactly representable zero separately.
        var breach = MeasurementAlertLimits.Evaluate(sample, metadata, low, high, DateTime.UtcNow);
        Assert.Equal(expected, breach?.Direction);
        if (breach != null) { Assert.Equal(-1.1, breach.Value, 9); Assert.Equal("A", breach.Unit); }
    }

    [Fact]
    public void BoundariesAndZeroAreValidAndNonfiniteLimitsAreRejected()
    {
        Assert.Null(MeasurementAlertLimits.Evaluate(0, new(), 0, 1, DateTime.UtcNow));
        Assert.Null(MeasurementAlertLimits.Evaluate(1, new(), 0, 1, DateTime.UtcNow));
        Assert.False(MeasurementAlertLimits.IsValid(double.NaN, null));
        Assert.False(MeasurementAlertLimits.IsValid(null, double.PositiveInfinity));
        Assert.False(MeasurementAlertLimits.IsValid(1, 1));
        Assert.True(MeasurementAlertLimits.IsValid(null, -1));
    }

    [Fact]
    public void BreachAndLimitsSurviveCopiesAndSignedTransport()
    {
        var breach = new MeasurementBreach("low", -1.1, -1, "A", DateTime.UtcNow);
        var host = new MonitorIP { LowThreshold = -1, HighThreshold = 2 };
        Assert.Equal(-1, new MonitorIP(host).LowThreshold);
        Assert.Equal(2, new UpdateMonitorIP(host).HighThreshold);
        var source = new MonitorStatusAlert { ID = 1, MeasurementBreach = breach };
        Assert.Equal(breach, new MonitorStatusAlert(source).MeasurementBreach);
        var message = new ProcessorDataObj { MonitorStatusAlerts = new() { source } };
        var payload = BackendMessageSignaturePayload.Create("test", "test", message);
        var received = JsonUtils.GetJsonObjectFromString<ProcessorDataObj>(JsonUtils.WriteJsonObjectToString(message));
        Assert.Equal(breach, Assert.Single(received!.MonitorStatusAlerts).MeasurementBreach);
        Assert.Equal(payload, BackendMessageSignaturePayload.Create("test", "test", received));
        var existing = new MonitorStatusAlert { ID = 1 };
        ProcessorDataBuilder.MergeMonitorStatusAlerts(received, new() { existing });
        Assert.Equal(breach, existing.MeasurementBreach);
    }
}
