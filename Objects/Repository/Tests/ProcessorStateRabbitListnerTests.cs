using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitor.Objects.Repository.Tests;

public class ProcessorStateRabbitListnerTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    public async Task UpdateProcessor_PropagatesCapabilityOnlyWhenSignatureIsValid(
        bool original, bool incoming, bool validSignature)
    {
        var cached = new ProcessorObj { AppID = "test", IsQuantumCapable = original, IsReady = true };
        var state = new ProcessorState();
        state.ResetConcurrentProcessorList(new List<ProcessorObj> { cached });
        var helper = new Mock<ISystemParamsHelper>();
        helper.Setup(x => x.GetSystemParams()).Returns(new SystemParams { ThisSystemUrl = new SystemUrl() });
        var files = new Mock<IFileRepo>();
        files.Setup(x => x.SaveStateJsonAsync("ProcessorList", It.IsAny<List<ProcessorObj>>()))
            .Returns(Task.CompletedTask);
        var verifier = new Mock<IBackendMessageSignatureVerifier>();
        verifier.Setup(x => x.VerifyAsync("updateProcessor", "processor-state",
                It.IsAny<IBackendSignedMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(validSignature);
        var listener = new ProcessorStateRabbitListner(NullLogger<RabbitListenerBase>.Instance,
            helper.Object, state, files.Object, verifier.Object);

        var result = await listener.UpdateProcessor(new ProcessorObj {
            AppID = "test", IsQuantumCapable = incoming, IsReady = false
        });

        Assert.Equal(validSignature, result.Success);
        Assert.Equal(validSignature ? incoming : original, cached.IsQuantumCapable);
        Assert.True(cached.IsReady);
        files.Verify(x => x.SaveStateJsonAsync("ProcessorList",
            It.Is<List<ProcessorObj>>(p => p.Exists(v => v.AppID == "test" && v.IsQuantumCapable == incoming))),
            validSignature ? Times.Once() : Times.Never());
    }
}
