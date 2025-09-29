using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using PuppeteerSharp;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class SiteHashConnectTests
    {
        [Fact]
        public async Task Connect_NoBrowserHost_ReportsError()
        {
            var connect = new SiteHashConnect(commandPath: null!, browserHost: null);
            connect.MpiStatic = new MPIStatic { Address = "example.com", Timeout = 5000, EndPointType = "sitehash" };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("BrowserHost is missing", connect.MpiConnect.Message);
            Assert.Equal("Browser Missing", connect.MpiConnect.PingInfo.Status);
        }

        private static Mock<IBrowserHost> CreateBrowserHost(string snapshot)
        {
            var mock = new Mock<IBrowserHost>();
            mock
                .Setup(h => h.RunWithPage(It.IsAny<Func<IPage, Task<string>>>(), It.IsAny<CancellationToken>()))
                .Returns<Func<IPage, Task<string>>, CancellationToken>((_, _) => Task.FromResult(snapshot));
            return mock;
        }

        [Fact]
        public async Task Connect_FirstRun_InitialisesHash()
        {
            var snapshot = "Hello world";
            var browserHost = CreateBrowserHost(snapshot);
            var connect = new SiteHashConnect("/commands", browserHost.Object);
            connect.MpiStatic = new MPIStatic { Address = "http://example.com", Timeout = 5000, EndPointType = "sitehash" };

            await connect.Connect();

            var expectedHash = HashHelper.ComputeSha256Hash(snapshot);
            Assert.True(connect.MpiConnect.IsUp);
            Assert.Equal("SiteHash initialized", connect.MpiConnect.PingInfo.Status);
            Assert.Equal(expectedHash, connect.MpiStatic.SiteHash);
            browserHost.Verify(h => h.RunWithPage(It.IsAny<Func<IPage, Task<string>>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Connect_SameHash_KeepsStatusOk()
        {
            var snapshot = "Consistent content";
            var expectedHash = HashHelper.ComputeSha256Hash(snapshot);
            var browserHost = CreateBrowserHost(snapshot);

            var connect = new SiteHashConnect("/commands", browserHost.Object);
            connect.MpiStatic = new MPIStatic
            {
                Address = "example.com",
                Timeout = 5000,
                EndPointType = "sitehash",
                SiteHash = expectedHash
            };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.Equal("SiteHash OK", connect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_Mismatch_FlagsFailure()
        {
            var snapshot = "Different";
            var browserHost = CreateBrowserHost(snapshot);

            var connect = new SiteHashConnect("/commands", browserHost.Object);
            connect.MpiStatic = new MPIStatic
            {
                Address = "example.com",
                Timeout = 5000,
                EndPointType = "sitehash",
                SiteHash = "previousHash"
            };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Contains("SiteHash mismatch", connect.MpiConnect.Message);
            Assert.Equal("SiteHash Mismatch", connect.MpiConnect.PingInfo.Status);
        }
    }
}
