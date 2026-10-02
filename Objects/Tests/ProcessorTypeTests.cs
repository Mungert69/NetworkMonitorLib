using System.Text.Json;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Repository.Helpers;
using Xunit;

namespace NetworkMonitor.Objects.Tests;

public class ProcessorTypeTests
{
    [Fact]
    public void AuthenticatedMqttRegistrationPreservesType()
    {
        const string owner = "11111111-1111-4111-8111-111111111111";
        var input = new ProcessorObj {
            AppID = owner + "-esp32.test", Owner = owner, Location = "desk",
            MaxLoad = 50, RabbitTopologyVersion = 2, PType = "ESP32-S3"
        };
        var route = "processor.register." + owner + "." + ProcessorRabbitTopology.GetRoutingId(input.AppID);
        Assert.Equal("ESP32-S3", ProcessorMqttRegistration.Validate(ProcessorMqttTopology.Exchange, route, input)!.PType);
    }
    [Fact]
    public void DefaultsAndExplicitNullAreEmpty()
    {
        Assert.Equal("", new ProcessorObj().PType);
        Assert.Equal("", new CProcessorReadyObj().PType);
        Assert.Equal("", JsonSerializer.Deserialize<ProcessorObj>("{\"PType\":null}")!.PType);
        Assert.Equal("", JsonSerializer.Deserialize<CProcessorReadyObj>("{\"PType\":null}")!.PType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CopyAndWireTypeAreIndependentOfSignatureCapability(bool quantum)
    {
        var original = new ProcessorObj { PType = "ESP32-S3", IsQuantumCapable = quantum };
        Assert.Equal("ESP32-S3", new ProcessorObj(original, false).PType);
        var destination = new ProcessorObj();
        destination.SetAllFields(original);
        Assert.Equal("ESP32-S3", destination.PType);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(original,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Assert.Equal("ESP32-S3", json.RootElement.GetProperty("pType").GetString());
        Assert.Equal(quantum, destination.IsQuantumCapable);
    }
}
