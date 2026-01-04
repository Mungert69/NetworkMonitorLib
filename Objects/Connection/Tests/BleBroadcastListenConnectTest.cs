using System.Threading;
using System.Threading.Tasks;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class BleBroadcastListenConnectTest
    {
        private static BleBroadcastListenConnect CreateConnect(ICmdProcessor? processor)
        {
            var provider = new Mock<ICmdProcessorProvider>();
            provider.Setup(p => p.GetProcessor("BleBroadcastListen")).Returns(processor);
            return new BleBroadcastListenConnect(provider.Object);
        }

        [Fact]
        public async Task Connect_NoProcessor_ReportsError()
        {
            var connect = CreateConnect(processor: null);
            connect.MpiStatic = new MPIStatic { Timeout = 2000, EndPointType = "blebroadcastlisten" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("No Command Processor Available", connect.MpiConnect.Message);
            Assert.Equal("Error", connect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_Success_NoAddress_AllowsListen()
        {
            ProcessorScanDataObj? sent = null;
            var processor = new Mock<ICmdProcessor>();
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .Callback<CancellationTokenSource, ProcessorScanDataObj>((_, data) => sent = data)
                .ReturnsAsync(new ResultObj { Success = true, Message = "listen ok" });

            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic
            {
                Address = "",
                Password = "",
                Timeout = 2000,
                EndPointType = "blebroadcastlisten"
            };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.Equal("BLE listen complete", connect.MpiConnect.PingInfo.Status);
            Assert.Contains("listen ok", connect.MpiConnect.Message);
            Assert.NotNull(sent);
            Assert.DoesNotContain("--address", sent!.Arguments);
        }

        [Fact]
        public async Task Connect_WithKey_IncludesKeyArgument()
        {
            ProcessorScanDataObj? sent = null;
            var processor = new Mock<ICmdProcessor>();
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .Callback<CancellationTokenSource, ProcessorScanDataObj>((_, data) => sent = data)
                .ReturnsAsync(new ResultObj { Success = true, Message = "listen ok" });

            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic
            {
                Password = "key",
                Timeout = 2000,
                EndPointType = "blebroadcastlisten"
            };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.NotNull(sent);
            Assert.Contains("--key \"key\"", sent!.Arguments);
        }
    }
}
