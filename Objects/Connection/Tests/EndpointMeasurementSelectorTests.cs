using NetworkMonitor.Connection;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection;

public class EndpointMeasurementSelectorTests
{
    [Theory]
    [InlineData("--format victron --metric battery_voltage", "V", 0.01)]
    [InlineData("--metric=BATTERY_VOLTAGE", "V", 0.01)]
    [InlineData("--metric \"battery_voltage\"", "V", 0.01)]
    [InlineData("battery_voltage battery_voltage", "V", 0.01)]
    [InlineData("battery_voltage battery_current", "raw value", 1)]
    [InlineData("battery_voltage battery_v", "raw value", 1)]
    [InlineData("prefix_battery_voltage", "raw value", 1)]
    [InlineData("battery_voltage_suffix", "raw value", 1)]
    [InlineData("battery-voltage", "raw value", 1)]
    [InlineData("--format victron", "raw value", 1)]
    [InlineData("--metric battery_current", "A", 0.1)]
    [InlineData("--metric load_current_a", "A", 0.1)]
    [InlineData("--metric yield_today", "kWh", 0.01)]
    [InlineData("--metric pv_power", "W", 1)]
    public void BLE_uses_exactly_one_distinct_whole_token(string args, string unit, double scale)
    {
        var result = EndpointMeasurementDefaults.Get("blebroadcast", args);
        Assert.Equal(unit, result.Unit);
        Assert.Equal(scale, result.Scale);
    }

    [Fact]
    public void Missing_general_definition_defaults_to_ms_and_unknown_types_do_not_match()
    {
        var definitions = new[] { new EndpointMeasurementMetadata("V", 0.01, "voltage") };
        Assert.Equal(new EndpointMeasurementMetadata(), EndpointMeasurementSelector.Resolve(definitions, "unknown"));
        Assert.Equal(definitions[0], EndpointMeasurementSelector.Resolve(definitions, "--reading voltage"));
    }
}
