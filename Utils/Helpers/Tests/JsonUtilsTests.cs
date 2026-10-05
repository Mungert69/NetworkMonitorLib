using System;
using System.Collections.Generic;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.DTOs;
using NetworkMonitor.Connection;
using System.Text.Json;
using NetworkMonitor.Utils;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class JsonUtilsTests
{
    [Fact]
    public void MeasurementTransportTypesHaveGeneratedMetadata()
    {
        var types = new[] {
            typeof(ProcessorDataObj), typeof(EndpointMeasurementDefinition),
            typeof(List<EndpointMeasurementDefinition>), typeof(TimingRatingThresholds),
            typeof(MonitorPingInfo), typeof(List<MonitorPingInfo>), typeof(HostResponseObj),
            typeof(PhysicalMeasurementResponse), typeof(PhysicalMeasurementReading),
            typeof(IReadOnlyList<PhysicalMeasurementReading>)
        };
        foreach (var type in types)
            Assert.True(SourceGenerationContext.Default.GetTypeInfo(type) != null,
                $"Missing generated JSON metadata for {type.FullName}");
    }

    [Fact]
    public void GetValueOrCoerce_Ushort_FromString()
    {
        var dict = new Dictionary<string, object>
        {
            ["port"] = "443"
        };

        var value = JsonUtils.GetValueOrCoerce<ushort?>(dict, "port");

        Assert.Equal((ushort)443, value);
    }

    [Fact]
    public void GetValueOrCoerce_Ushort_FromJsonElement()
    {
        using var doc = JsonDocument.Parse("{\"port\": 8443}");
        var dict = new Dictionary<string, object>
        {
            ["port"] = doc.RootElement.GetProperty("port")
        };

        var value = JsonUtils.GetValueOrCoerce<ushort?>(dict, "port");

        Assert.Equal((ushort)8443, value);
    }
}
