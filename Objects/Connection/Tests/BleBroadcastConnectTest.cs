using System.Threading;
using System.Threading.Tasks;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class BleBroadcastConnectTest
    {
        private static BleBroadcastConnect CreateConnect(ICmdProcessor? processor)
        {
            var provider = new Mock<ICmdProcessorProvider>();
            provider.Setup(p => p.GetProcessor("BleBroadcast")).Returns(processor);
            return new BleBroadcastConnect(provider.Object);
        }

        [Fact]
        public async Task Connect_NoProcessor_ReportsError()
        {
            var connect = CreateConnect(processor: null);
            connect.MpiStatic = new MPIStatic { Address = "AA:BB:CC:DD:EE:FF", Password = "key", Timeout = 2000, EndPointType = "blebroadcast" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("No Command Processor Available", connect.MpiConnect.Message);
            Assert.Equal("Error", connect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_MissingAddress_ReportsError()
        {
            var processor = new Mock<ICmdProcessor>();
            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic { Address = "", Password = "key", Timeout = 2000, EndPointType = "blebroadcast" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("Missing BLE address", connect.MpiConnect.Message);
            Assert.Equal("Error", connect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_MissingKey_ReportsError()
        {
            var processor = new Mock<ICmdProcessor>();
            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic { Address = "AA:BB:CC:DD:EE:FF", Password = "", Timeout = 2000, EndPointType = "blebroadcast" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("Missing BLE key", connect.MpiConnect.Message);
            Assert.Equal("Error", connect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_Success_SetsStatus()
        {
            ProcessorScanDataObj? sent = null;
            var processor = new Mock<ICmdProcessor>();
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .Callback<CancellationTokenSource, ProcessorScanDataObj>((_, data) => sent = data)
                .ReturnsAsync(new ResultObj { Success = true, Message = "payload ok" });

            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic
            {
                Address = "AA:BB:CC:DD:EE:FF",
                Password = "key",
                Timeout = 2000,
                EndPointType = "blebroadcast"
            };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.Equal("BLE broadcast received", connect.MpiConnect.PingInfo.Status);
            Assert.Contains("payload ok", connect.MpiConnect.Message);
            Assert.NotNull(sent);
            Assert.Contains("--address \"AA:BB:CC:DD:EE:FF\"", sent!.Arguments);
            Assert.Contains("--key \"key\"", sent.Arguments);
        }
    }
}
