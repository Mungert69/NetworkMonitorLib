using System.Threading;
using System.Threading.Tasks;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class NmapCmdConnectTests
    {
        private static NmapCmdConnect CreateConnect(ICmdProcessor? processor, string baseArg)
        {
            var provider = new Mock<ICmdProcessorProvider>();
            provider.Setup(p => p.GetProcessor("Nmap")).Returns(processor);
            return new NmapCmdConnect(provider.Object, baseArg);
        }

        [Fact]
        public async Task Connect_NoProcessor_ReportsError()
        {
            var connect = CreateConnect(processor: null, baseArg: "-sV");
            connect.MpiStatic = new MPIStatic { Address = "https://example.com", Port = 0, Timeout = 2000, EndPointType = "nmap" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("No Command Processor Available", connect.MpiConnect.Message);
            Assert.Equal("Error", connect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_HostUp_SetsStatus()
        {
            ProcessorScanDataObj? sent = null;
            var processor = new Mock<ICmdProcessor>();
            var nmapOutput = @"Starting Nmap
Nmap scan report for example.com (1.2.3.4)
Host is up (0.10s latency).
PORT   STATE SERVICE
80/tcp open  http";

            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .Callback<CancellationTokenSource, ProcessorScanDataObj>((_, data) => sent = data)
                .ReturnsAsync(new ResultObj { Success = true, Message = nmapOutput });

            var connect = CreateConnect(processor.Object, "-sV");
            connect.MpiStatic = new MPIStatic { Address = "https://example.com", Port = 443, Timeout = 5000, EndPointType = "nmap" };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.Equal("Port/s open", connect.MpiConnect.PingInfo.Status);
            Assert.NotNull(sent);
            Assert.Contains("-sV", sent!.Arguments);
            Assert.Contains("--system-dns -p 443 example.com", sent.Arguments);
        }

        [Fact]
        public async Task Connect_VulnerabilitiesFound_FlagsFailure()
        {
            var processor = new Mock<ICmdProcessor>();
            var nmapOutput = @"Nmap scan report for example.com (1.2.3.4)
Host is up (0.10s latency).
PORT   STATE SERVICE
443/tcp open  https
VULNERABLE: Known CVE";

            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .ReturnsAsync(new ResultObj { Success = true, Message = nmapOutput });

            var connect = CreateConnect(processor.Object, "--script vuln");
            connect.MpiStatic = new MPIStatic { Address = "example.com", Port = 0, Timeout = 5000, EndPointType = "nmapvuln" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("vulnerabilities found", connect.MpiConnect.PingInfo.Status);
            Assert.Equal(ushort.MaxValue, connect.MpiConnect.PingInfo.RoundTripTime);
        }
    }
}
