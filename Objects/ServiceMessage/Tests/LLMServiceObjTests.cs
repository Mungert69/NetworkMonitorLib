using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.ServiceMessage;

public class LLMServiceObjTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CopyConstructor_PreservesStackPopOrder(int levels)
    {
        var original = CreateServiceObj(levels);

        var copy = new LLMServiceObj(original);

        AssertStackOrderEqual(original, copy);
        AssertCurrentStateEqual(original, copy);

        while (original.LlmStack.Count > 0)
        {
            original.PopLlm();
            copy.PopLlm();

            AssertStackOrderEqual(original, copy);
            AssertCurrentStateEqual(original, copy);
        }
    }

    [Fact]
    public void CopyConstructor_CreatesIndependentStacks()
    {
        var original = CreateServiceObj(2);
        var originalLlmPopOrder = original.LlmStack.ToArray();
        var originalMessageIdPopOrder = original.MessageIDStack.ToArray();
        var originalFunctionCallIdPopOrder = original.FunctionCallIdStack.ToArray();
        var originalFunctionNamePopOrder = original.FunctionNameStack.ToArray();
        var originalProcessedPopOrder = original.IsProcessedStack.ToArray();

        var copy = new LLMServiceObj(original);
        copy.PopLlm();

        Assert.Equal(originalLlmPopOrder, original.LlmStack.ToArray());
        Assert.Equal(originalMessageIdPopOrder, original.MessageIDStack.ToArray());
        Assert.Equal(originalFunctionCallIdPopOrder, original.FunctionCallIdStack.ToArray());
        Assert.Equal(originalFunctionNamePopOrder, original.FunctionNameStack.ToArray());
        Assert.Equal(originalProcessedPopOrder, original.IsProcessedStack.ToArray());
        Assert.Single(copy.LlmStack);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedCall_ReturnsToSameCallerAcrossRepeatedJsonHops(bool sourceGenerated)
    {
        var expected = CreateServiceObj(2);
        var actual = new LLMServiceObj(expected);
        for (var level = 2; level >= 0; level--)
        {
            for (var hop = 0; hop < 3; hop++)
            {
                actual = sourceGenerated
                    ? (LLMServiceObj)System.Text.Json.JsonSerializer.Deserialize(
                        System.Text.Json.JsonSerializer.Serialize(actual, typeof(LLMServiceObj), NetworkMonitor.Objects.SourceGenerationContext.Default),
                        typeof(LLMServiceObj), NetworkMonitor.Objects.SourceGenerationContext.Default)!
                    : System.Text.Json.JsonSerializer.Deserialize<LLMServiceObj>(System.Text.Json.JsonSerializer.Serialize(actual))!;
                actual = new LLMServiceObj(actual);
                AssertStackOrderEqual(expected, actual);
                AssertCurrentStateEqual(expected, actual);
                Assert.Equal(expected.RootMessageID, actual.RootMessageID);
                Assert.Equal(expected.LlmChainStartName, actual.LlmChainStartName);
                Assert.Equal(expected.FirstFunctionName, actual.FirstFunctionName);
            }
            expected.PopLlm();
            actual.PopLlm();
            AssertCurrentStateEqual(expected, actual);
        }
    }

    private static LLMServiceObj CreateServiceObj(int levels)
    {
        var serviceObj = new LLMServiceObj
        {
            SourceLlm = "PrimaryLLM",
            DestinationLlm = "PrimaryLLM",
            MessageID = "root-message",
            FunctionCallId = "root-call",
            FunctionName = "root-function",
            IsProcessed = true
        };

        if (levels >= 1)
        {
            serviceObj.PushLmm(
                "ExpertOne",
                "expert-one-call",
                "expert-one-function",
                "expert-one-message",
                false);
        }

        if (levels >= 2)
        {
            serviceObj.PushLmm(
                "ExpertTwo",
                "expert-two-call",
                "expert-two-function",
                "expert-two-message",
                true);
        }

        return serviceObj;
    }

    private static void AssertStackOrderEqual(LLMServiceObj expected, LLMServiceObj actual)
    {
        Assert.Equal(expected.LlmStack.ToArray(), actual.LlmStack.ToArray());
        Assert.Equal(expected.MessageIDStack.ToArray(), actual.MessageIDStack.ToArray());
        Assert.Equal(expected.FunctionCallIdStack.ToArray(), actual.FunctionCallIdStack.ToArray());
        Assert.Equal(expected.FunctionNameStack.ToArray(), actual.FunctionNameStack.ToArray());
        Assert.Equal(expected.IsProcessedStack.ToArray(), actual.IsProcessedStack.ToArray());
    }

    private static void AssertCurrentStateEqual(LLMServiceObj expected, LLMServiceObj actual)
    {
        Assert.Equal(expected.SourceLlm, actual.SourceLlm);
        Assert.Equal(expected.DestinationLlm, actual.DestinationLlm);
        Assert.Equal(expected.MessageID, actual.MessageID);
        Assert.Equal(expected.FunctionCallId, actual.FunctionCallId);
        Assert.Equal(expected.FunctionName, actual.FunctionName);
    }
}
