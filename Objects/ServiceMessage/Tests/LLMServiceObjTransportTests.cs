using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NetworkMonitor.Objects.Repository;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace NetworkMonitor.Objects.ServiceMessage.Tests;

public class LLMServiceObjTransportTests
{
    // These are the construction paths used by FunctionExecutor, OpenAIRunner,
    // token broadcasters, TaskManager, and LLMService.UpdateServiceObjs.
    public static IEnumerable<object[]> Cases()
    {
        foreach (int depth in new[] { 0, 1, 2, 3, 8 })
        foreach (int copy in new[] { 0, 1, 2, 3 })
        foreach (int transport in new[] { 0, 1, 2, 3 })
            yield return new object[] { depth, copy, transport };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CopiesAndEveryReceivePreserveAllStacksAndSubsequentPops(int depth, int copy, int transport)
    {
        var expected = Message(depth);
        var actual = Message(depth);
        for (int remaining = depth; remaining >= 0; remaining--)
        {
            // Check odd AND even hop counts; two legacy reversals cancel.
            for (int hop = 0; hop < 4; hop++)
            {
                actual = Copy(actual, copy);
                Same(expected, actual);
                actual = Transfer(actual, transport);
                Same(expected, actual);
                actual = Copy(actual, copy);
                Same(expected, actual);
            }
            expected.PopLlm();
            actual.PopLlm();
            Same(expected, actual);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshConstructorsInitializeIndependentEmptyStacks(bool configure)
    {
        var a = configure ? new LLMServiceObj(fs => fs.SetAsCall()) : new LLMServiceObj();
        var b = configure ? new LLMServiceObj(fs => fs.SetAsCall()) : new LLMServiceObj();
        Assert.Empty(a.LlmStack);
        Assert.Empty(a.MessageIDStack);
        Assert.Empty(a.FunctionNameStack);
        Assert.Empty(a.FunctionCallIdStack);
        Assert.Empty(a.IsProcessedStack);
        a.LlmStack.Push("a"); a.MessageIDStack.Push("a"); a.FunctionNameStack.Push("a");
        a.FunctionCallIdStack.Push("a"); a.IsProcessedStack.Push(true);
        Assert.Empty(b.LlmStack); Assert.Empty(b.MessageIDStack); Assert.Empty(b.FunctionNameStack);
        Assert.Empty(b.FunctionCallIdStack); Assert.Empty(b.IsProcessedStack);
        Assert.Equal(configure, a.IsFunctionCall);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AllCopyPathsOwnTheirStacksAndFunctionState(int mode)
    {
        var original = Message(3);
        original.SetAsCall();
        var copy = Copy(original, mode);
        copy.SetAsResponseComplete();
        foreach (var property in StackNames)
            Assert.NotSame(typeof(LLMServiceObj).GetProperty(property)!.GetValue(original),
                typeof(LLMServiceObj).GetProperty(property)!.GetValue(copy));
        copy.PopLlm();
        Same(Message(3), original);
        Assert.True(original.IsFunctionCall);
        Assert.True(copy.IsFunctionCallResponse);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ConfiguredCopyAndTransportPreserveRequestedFunctionState(int state)
    {
        var original = Message(3);
        var copy = new LLMServiceObj(original, fs => {
            if (state == 0) fs.SetAsCall();
            if (state == 1) fs.SetAsResponseComplete();
            if (state == 2) fs.SetAsResponseRunning();
            if (state == 3) fs.SetAsResponseErrorComplete();
        });
        var received = Transfer(copy, 2);
        Same(original, received);
        Assert.Equal(copy.GetFunctionStateString(), received.GetFunctionStateString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LegacyReceiverReverses_CopyPreservesReversal_SecondLegacyHopCancels(int copyMode)
    {
        var original = Message(3);
        var once = LegacyReceive(original);
        Assert.Equal(original.MessageIDStack.Reverse(), once.MessageIDStack);
        Assert.NotEqual(original.RootMessageID, once.RootMessageID);
        var copy = Copy(once, copyMode);
        Same(once, copy);
        Same(original, LegacyReceive(copy));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void MixedReceiverVersionsReverseOncePerLegacyReceiver(bool oldFirst, bool oldSecond, bool reversed)
    {
        var original = Message(3);
        var first = oldFirst ? LegacyReceive(original) : Transfer(original, 2);
        var copy = new LLMServiceObj(first);
        var second = oldSecond ? LegacyReceive(copy) : Transfer(copy, 2);
        Assert.Equal(reversed ? original.MessageIDStack.Reverse() : original.MessageIDStack, second.MessageIDStack);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void SignedPayloadSurvivesCloudEventAndNestedProcessorCommand(int depth)
    {
        foreach (int transport in new[] { 2, 3 })
        {
            var original = Message(depth);
            original.BackendSignature = "test-signature";
            var payload = BackendMessageSignaturePayload.Create("llmUserInput", "expert", original);
            var received = Transfer(original, transport);
            Assert.Equal("test-signature", received.BackendSignature);
            Assert.Equal(payload, BackendMessageSignaturePayload.Create("llmUserInput", "expert", received));
            received.MessageIDStack.Push("tampered-parent");
            Assert.NotEqual(payload, BackendMessageSignaturePayload.Create("llmUserInput", "expert", received));
        }
    }

    private static readonly string[] StackNames = {
        nameof(LLMServiceObj.LlmStack), nameof(LLMServiceObj.MessageIDStack),
        nameof(LLMServiceObj.FunctionCallIdStack), nameof(LLMServiceObj.FunctionNameStack),
        nameof(LLMServiceObj.IsProcessedStack)
    };

    // Remove only the five property converters to reproduce the pre-fix reader.
    // This retains the real DTO, constructors and copy implementation.
    private static readonly JsonSerializerOptions LegacyOptions = new() {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = {
            info => {
                if (info.Type == typeof(LLMServiceObj))
                    foreach (var property in info.Properties)
                        if (StackNames.Contains(property.Name)) property.CustomConverter = null;
            }
        } }
    };

    private static LLMServiceObj LegacyReceive(LLMServiceObj value)
    {
        using var doc = JsonDocument.Parse(Wire(value));
        return JsonSerializer.Deserialize<LLMServiceObj>(doc.RootElement.GetProperty("data").GetRawText(), LegacyOptions)!;
    }

    private static LLMServiceObj Copy(LLMServiceObj value, int mode) => mode switch {
        0 => value,
        1 => new LLMServiceObj(value),
        2 => new LLMServiceObj(value, fs => fs.SetAsResponseComplete()),
        3 => CopyIntoExisting(value),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static LLMServiceObj CopyIntoExisting(LLMServiceObj value)
    {
        var target = Message(5);
        target.Copy(value);
        return target;
    }

    private static LLMServiceObj Message(int depth)
    {
        var value = new LLMServiceObj { SourceLlm = "source", DestinationLlm = "destination",
            MessageID = "current-message", FunctionName = "current-function", FunctionCallId = "current-call", IsProcessed = true };
        for (int i = 0; i < depth; i++)
        {
            value.LlmStack.Push($"parent-{i}"); value.MessageIDStack.Push($"message-{i}");
            value.FunctionNameStack.Push($"function-{i}"); value.FunctionCallIdStack.Push($"call-{i}");
            value.IsProcessedStack.Push(i == 0);
        }
        return value;
    }

    private static void Same(LLMServiceObj expected, LLMServiceObj actual)
    {
        Assert.Equal(expected.LlmStack.ToArray(), actual.LlmStack.ToArray());
        Assert.Equal(expected.MessageIDStack.ToArray(), actual.MessageIDStack.ToArray());
        Assert.Equal(expected.FunctionNameStack.ToArray(), actual.FunctionNameStack.ToArray());
        Assert.Equal(expected.FunctionCallIdStack.ToArray(), actual.FunctionCallIdStack.ToArray());
        Assert.Equal(expected.IsProcessedStack.ToArray(), actual.IsProcessedStack.ToArray());
        Assert.Equal(expected.SourceLlm, actual.SourceLlm); Assert.Equal(expected.DestinationLlm, actual.DestinationLlm);
        Assert.Equal(expected.MessageID, actual.MessageID); Assert.Equal(expected.FunctionName, actual.FunctionName);
        Assert.Equal(expected.FunctionCallId, actual.FunctionCallId);
        Assert.Equal(expected.RootMessageID, actual.RootMessageID); Assert.Equal(expected.LlmChainStartName, actual.LlmChainStartName);
        Assert.Equal(expected.FirstFunctionName, actual.FirstFunctionName); Assert.Equal(expected.IsPrimaryLlm, actual.IsPrimaryLlm);
    }

    private static byte[] Wire(object value) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CloudEvent {
        id = "test-event", type = value.GetType().Name, source = "test-service", time = DateTime.UnixEpoch, data = value
    }, typeof(CloudEvent), SourceGenerationContext.Default));

    private static LLMServiceObj Transfer(LLMServiceObj value, int transport)
    {
        if (transport == 0) return JsonSerializer.Deserialize<LLMServiceObj>(JsonSerializer.Serialize(value))!;
        if (transport == 1) return (LLMServiceObj)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(value, typeof(LLMServiceObj), SourceGenerationContext.Default),
            typeof(LLMServiceObj), SourceGenerationContext.Default)!;
        var listener = new Listener();
        if (transport == 2) return listener.Parse<LLMServiceObj>(Wire(value));
        return listener.Parse<ProcessorScanDataObj>(Wire(new ProcessorScanDataObj { LlmServiceObj = value })).LlmServiceObj;
    }

    private sealed class Listener : RabbitListenerBase
    {
        public Listener() : base(NullLogger.Instance, new SystemUrl { RequirePublisherUserId = true }) { }
        protected override void InitRabbitMQObjs() { }
        protected override Task<ResultObj> DeclareConsumers() => Task.FromResult(new ResultObj { Success = true });
        public T Parse<T>(byte[] wire) where T : class => ConvertToObject<T>(null, new BasicDeliverEventArgs(
            "test", 1, false, "llmUserInput", "", new BasicProperties { UserId = "test-publisher" }, wire, CancellationToken.None))!;
    }
}
