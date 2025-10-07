using System.Threading;
using System.Threading.Tasks;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class HugSpaceKeepAliveConnectTests
    {
        private static HugSpaceKeepAliveConnect CreateConnect(ICmdProcessor? processor, string baseArg = "")
        {
            var provider = new Mock<ICmdProcessorProvider>();
            provider.Setup(p => p.GetProcessor("HugSpaceKeepAlive")).Returns(processor);
            return new HugSpaceKeepAliveConnect(provider.Object, baseArg);
        }

        [Fact]
        public async Task Connect_NoProcessor_ReportsError()
        {
            var connect = CreateConnect(null);
            connect.MpiStatic = new MPIStatic { Address = "huggingface.co/spaces/test", Port = 0, Timeout = 2000, EndPointType = "dailyhugkeepalive" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("No Command Processor Available", connect.MpiConnect.Message);
            Assert.Equal("Error", connect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_Success_ReportsAlive()
        {
            ProcessorScanDataObj? captured = null;
            var processor = new Mock<ICmdProcessor>();
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .Callback<CancellationTokenSource, ProcessorScanDataObj>((_, data) => captured = data)
                .ReturnsAsync(new ResultObj { Success = true, Message = "Space appears to be running (no restart needed)." });

            var connect = CreateConnect(processor.Object, "--wake");
            connect.MpiStatic = new MPIStatic { Address = "hf.space/myspace", Port = 8080, Timeout = 4000, EndPointType = "dailyhugkeepalive" };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.Equal("Hug Space is Alive", connect.MpiConnect.PingInfo.Status);
            Assert.NotNull(captured);
            Assert.Contains("--url https://hf.space:8080/myspace", captured!.Arguments);
            Assert.Contains("--wake", captured.Arguments);
        }

        [Fact]
        public async Task Connect_Error_ResponseSetsFailure()
        {
            var processor = new Mock<ICmdProcessor>();
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .ReturnsAsync(new ResultObj { Success = false, Message = "Error: offline" });

            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic { Address = "hf.space/offline", Port = 0, Timeout = 4000, EndPointType = "dailyhugkeepalive" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Equal("Hug Space Keep Alive Failed", connect.MpiConnect.PingInfo.Status);
            Assert.Contains("offline", connect.MpiConnect.Message);
        }

        [Fact]
        public async Task Connect_RestartTimeoutMessage_TreatedAsFailure()
        {
            var processor = new Mock<ICmdProcessor>();
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .ReturnsAsync(new ResultObj
                {
                    Success = true,
                    Message = "Clicked restart, but the Space did not become ready within the timeout."
                });

            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic { Address = "hf.space/pending", Port = 0, Timeout = 4000, EndPointType = "dailyhugkeepalive" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Equal("Hug Space Keep Alive Failed", connect.MpiConnect.PingInfo.Status);
            Assert.Contains("did not become ready", connect.MpiConnect.Message);
        }
    }
}
