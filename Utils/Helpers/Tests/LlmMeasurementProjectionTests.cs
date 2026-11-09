using System.Text.Json;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class LlmMeasurementProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContextIsOptionalAndSharedOnlyByIdenticalDefinitions(bool detail)
    {
        var voltage = EndpointMeasurementDefaults.Get("blebroadcast", "--format victron --metric battery_voltage");
        var rows = new[] { voltage, voltage with { }, voltage with { AnalysisGuidance = "Different device guidance" } }
            .Select((definition, index) => new MonitorPingInfo {
                ID = index, EndPointType = "blebroadcast", Args = "--format victron --metric battery_voltage",
                Unit = "V", Scale = .01, PacketsRecieved = 1, RoundTripTimeAverage = 1320,
                MeasurementContext = definition
            }).ToArray();
        var result = JsonSerializer.SerializeToElement(LlmMeasurementProjection.Project(rows, detail));
        var data = result.GetProperty("data");
        Assert.Equal(13.2, data[0].GetProperty("measurement_average").GetDouble(), 6);
        Assert.Equal(detail, result.TryGetProperty("measurement_contexts", out var contexts));
        Assert.Equal(detail, data[0].TryGetProperty("measurement_context_id", out _));
        if (detail)
        {
            Assert.Equal(2, contexts.EnumerateObject().Count());
            Assert.Equal(data[0].GetProperty("measurement_context_id").GetString(), data[1].GetProperty("measurement_context_id").GetString());
            Assert.NotEqual(data[0].GetProperty("measurement_context_id").GetString(), data[2].GetProperty("measurement_context_id").GetString());
            foreach (var row in data.EnumerateArray())
                Assert.True(contexts.TryGetProperty(row.GetProperty("measurement_context_id").GetString()!, out _));
        }
        Assert.All(rows, row => Assert.Equal(1320, row.RoundTripTimeAverage));
        Assert.False(JsonSerializer.SerializeToElement(rows[0]).TryGetProperty("MeasurementContext", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleRowAndBatchShareTheSameObjectProjection(bool detail)
    {
        var source = new MonitorPingInfo {
            ID = 42, EndPointType = "blebroadcast", Unit = "V", Scale = .01,
            Args = "--format victron --metric battery_voltage", PacketsRecieved = 1,
            RoundTripTimeAverage = 1320, Port = 123
        };
        var row = LlmMeasurementRowProjector.Project(source, detail);
        var batch = LlmMeasurementProjection.Project(new[] { source }, detail);
        row.MeasurementContextId = batch.Data[0].MeasurementContextId;
        Assert.Equal(JsonSerializer.Serialize(row), JsonSerializer.Serialize(batch.Data[0]));
        var json = JsonSerializer.SerializeToElement(row);
        Assert.Equal(detail, json.TryGetProperty("dataset_id", out _));
        Assert.Equal(detail, json.TryGetProperty("status", out _));
        Assert.Equal(detail, json.TryGetProperty("alert_flag", out _));
    }

    [Fact]
    public void MissingAgentDefinitionDoesNotInventMeaning()
    {
        var result = JsonSerializer.SerializeToElement(LlmMeasurementProjection.Project(new[] {
            new MonitorPingInfo { EndPointType = "custom", Unit = "A" }
        }, true));
        Assert.Equal("unspecified", result.GetProperty("measurement_contexts").GetProperty("m1").GetProperty("analysis_kind").GetString());
    }
}
