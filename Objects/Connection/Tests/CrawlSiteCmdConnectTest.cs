using System.Threading;
using System.Threading.Tasks;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class CrawlSiteCmdConnectTests
    {
        private static CrawlSiteCmdConnect CreateConnect(ICmdProcessor? processor, string baseArg = "--max_depth 3")
        {
            var provider = new Mock<ICmdProcessorProvider>();
            provider.Setup(p => p.GetProcessor("CrawlSite")).Returns(processor);
            return new CrawlSiteCmdConnect(provider.Object, baseArg);
        }

        [Fact]
        public async Task Connect_NoProcessor_ReportsError()
        {
            var connect = CreateConnect(processor: null);
            connect.MpiStatic = new MPIStatic { Address = "example.com", Port = 0, Timeout = 1000, EndPointType = "crawlsite" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("No Command Processor Available", connect.MpiConnect.Message);
            Assert.Equal("Error", connect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_SuccessfulCrawl_SetsStatus()
        {
            ProcessorScanDataObj? captured = null;
            var processor = new Mock<ICmdProcessor>();
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .Callback<CancellationTokenSource, ProcessorScanDataObj>((_, data) => captured = data)
                .ReturnsAsync(new ResultObj { Success = true, Message = "Scrolled 10 pages" });

            var connect = CreateConnect(processor.Object, "--base");
            connect.MpiStatic = new MPIStatic { Address = "example.com", Port = 8080, Timeout = 2000, EndPointType = "crawlsite" };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.Equal("Site Crawl Complete", connect.MpiConnect.PingInfo.Status);
            Assert.Contains("Scrolled", connect.MpiConnect.Message);
            Assert.NotNull(captured);
            Assert.Contains("--url https://example.com:8080", captured!.Arguments);
            Assert.Contains("--base", captured.Arguments);
        }

        [Fact]
        public async Task Connect_FailedCrawl_ReportsFailure()
        {
            var processor = new Mock<ICmdProcessor>();
            processor
                .Setup(p => p.QueueCommand(It.IsAny<CancellationTokenSource>(), It.IsAny<ProcessorScanDataObj>()))
                .ReturnsAsync(new ResultObj { Success = true, Message = "Error: crawl failed" });

            var connect = CreateConnect(processor.Object);
            connect.MpiStatic = new MPIStatic { Address = "example.com", Port = 0, Timeout = 2000, EndPointType = "crawlsite" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Equal("Crawl Failed", connect.MpiConnect.PingInfo.Status);
            Assert.Contains("crawl failed", connect.MpiConnect.Message, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
