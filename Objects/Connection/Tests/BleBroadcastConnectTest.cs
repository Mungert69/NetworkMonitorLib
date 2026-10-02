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
        public async Task Connect_MissingKey_OmitsKeyArgument()
        {
            var processor = new Mock<ICmdProcessor>();
            ProcessorScanDataObj? sent = null;
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .Callback<CancellationTokenSource, ProcessorScanDataObj>((_, data) => sent = data)
                .ReturnsAsync(new ResultObj { Success = true, Message = "payload ok" });
            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic { Address = "AA:BB:CC:DD:EE:FF", Password = "", Timeout = 2000, EndPointType = "blebroadcast" };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.NotNull(sent);
            Assert.Contains("--address \"AA:BB:CC:DD:EE:FF\"", sent!.Arguments);
            Assert.DoesNotContain("--key", sent.Arguments);
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

        [Theory]
        [InlineData("pv_power", "BLE pv_power", 123, 456)]
        [InlineData("pvpower", "BLE pv_power", 123, 456)]
        [InlineData("pv", "BLE pv_power", 123, 456)]
        [InlineData("battery_voltage", "BLE battery_voltage", 1367, 1368)]
        [InlineData("battery_voltage_v", "BLE battery_voltage", 1367, 1368)]
        [InlineData("battery_v", "BLE battery_voltage", 1367, 1368)]
        [InlineData("battery_current", "BLE battery_current", 25, 26)]
        [InlineData("battery_current_a", "BLE battery_current", 25, 26)]
        [InlineData("battery_a", "BLE battery_current", 25, 26)]
        [InlineData("load_current", "BLE load_current", 30, 31)]
        [InlineData("load_current_a", "BLE load_current", 30, 31)]
        [InlineData("load_a", "BLE load_current", 30, 31)]
        [InlineData("yield_today", "BLE yield_today", 300, 301)]
        [InlineData("yield", "BLE yield_today", 300, 301)]
        [InlineData("yield_today_kwh", "BLE yield_today", 300, 301)]
        public async Task Connect_ChangingMetricReadings_KeepPingStatusFixed(
            string metric, string expectedStatus, int firstValue, int secondValue)
        {
            const string firstOutput = "Battery voltage: 13.67 V; Battery current: 2.5 A; " +
                "Yield today: 3.00 kWh; PV power: 123 W; Load current: 3.0 A";
            const string secondOutput = "Battery voltage: 13.68 V; Battery current: 2.6 A; " +
                "Yield today: 3.01 kWh; PV power: 456 W; Load current: 3.1 A";
            var processor = new Mock<ICmdProcessor>();
            processor
                .SetupSequence(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .ReturnsAsync(new ResultObj { Success = true, Message = firstOutput })
                .ReturnsAsync(new ResultObj { Success = true, Message = secondOutput });
            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic
            {
                Address = "AA:BB:CC:DD:EE:FF",
                Password = "key",
                Args = $"--format victron --metric {metric}",
                Timeout = 2000,
                EndPointType = "blebroadcast"
            };

            foreach (var (output, value) in new[] { (firstOutput, firstValue), (secondOutput, secondValue) })
            {
                await connect.Connect();

                Assert.True(connect.MpiConnect.IsUp);
                Assert.Equal(expectedStatus, connect.MpiConnect.PingInfo.Status);
                Assert.Equal((ushort)value, connect.MpiConnect.PingInfo.RoundTripTime);
                Assert.Contains(output, connect.MpiConnect.Message);
            }
        }
    }
}
