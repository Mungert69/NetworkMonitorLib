using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace NetworkMonitor.Objects.ServiceMessage.Tests;

public class BackendSignatureStackTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void RabbitRoundTripPreservesStacksAndSignedPayload(int count)
    {
        var message = new ProcessorScanDataObj { AgentID = "agent-1", BackendSignature = "signature" };
        var llm = message.LlmServiceObj;
        for (var i = 0; i < count; i++)
        {
            llm.LlmStack.Push($"llm-{i}");
            llm.FunctionCallIdStack.Push($"call-{i}");
            llm.FunctionNameStack.Push($"function-{i}");
            llm.MessageIDStack.Push($"message-{i}");
            llm.IsProcessedStack.Push(i == 0);
        }
        var before = BackendMessageSignaturePayload.Create("processorCommand", "agent-1", message);
        var json = JsonSerializer.Serialize(message, typeof(ProcessorScanDataObj), SourceGenerationContext.Default);
        // Wire order must remain compatible with existing senders.
        using var document = JsonDocument.Parse(json);
        var wireStack = document.RootElement.GetProperty("LlmServiceObj").GetProperty("LlmStack");
        if (count > 0) Assert.Equal($"llm-{count - 1}", wireStack[0].GetString());
        var received = (ProcessorScanDataObj)JsonSerializer.Deserialize(json, typeof(ProcessorScanDataObj), SourceGenerationContext.Default)!;
        Assert.Equal(llm.LlmStack.ToArray(), received.LlmServiceObj.LlmStack.ToArray());
        Assert.Equal(llm.FunctionCallIdStack.ToArray(), received.LlmServiceObj.FunctionCallIdStack.ToArray());
        Assert.Equal(llm.FunctionNameStack.ToArray(), received.LlmServiceObj.FunctionNameStack.ToArray());
        Assert.Equal(llm.MessageIDStack.ToArray(), received.LlmServiceObj.MessageIDStack.ToArray());
        Assert.Equal(llm.IsProcessedStack.ToArray(), received.LlmServiceObj.IsProcessedStack.ToArray());
        Assert.Equal(before, BackendMessageSignaturePayload.Create("processorCommand", "agent-1", received));
        received.Arguments = "tampered";
        Assert.NotEqual(before, BackendMessageSignaturePayload.Create("processorCommand", "agent-1", received));
    }
}
