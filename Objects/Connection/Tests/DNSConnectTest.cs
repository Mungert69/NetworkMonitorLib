using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    // Small test double that overrides the virtual ResolveAsync
    internal sealed class DnsConnectSpy : DNSConnect
    {
        private readonly Func<string, CancellationToken, Task<IPAddress[]>> _stub;

        public DnsConnectSpy(Func<string, CancellationToken, Task<IPAddress[]>> stub)
            => _stub = stub;

        protected override Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
            => _stub(host, ct);
    }

    public class DNSConnect_VirtualSeam_Tests
    {
        [Fact]
        public async Task Connect_sets_status_when_ips_returned()
        {
            var fakeIps = new[] { IPAddress.Parse("1.2.3.4") };
            var sut = new DnsConnectSpy((_, __) => Task.FromResult(fakeIps))
            {
                MpiStatic = new MPIStatic { Address = "example.com" }
            };

            await sut.Connect();

            Assert.True(sut.MpiConnect.IsUp);
            Assert.Equal("Found IP Addresses", sut.MpiConnect.PingInfo.Status);
            Assert.Contains("1.2.3.4", sut.MpiConnect.Message);
        }

        [Fact]
        public async Task Connect_sets_error_when_resolver_returns_empty()
        {
            var sut = new DnsConnectSpy((_, __) => Task.FromResult(Array.Empty<IPAddress>()))
            {
                MpiStatic = new MPIStatic { Address = "example.com", EndPointType = "dns" }
            };

            await sut.Connect();

            Assert.False(sut.MpiConnect.IsUp);
            Assert.Equal("Exception", sut.MpiConnect.PingInfo.Status);
            Assert.Equal("DNS: Failed to connect: No IP addresses found for host", sut.MpiConnect.Message);
        }

        [Fact]
        public async Task Connect_sets_timeout_when_resolver_throws_cancelled()
        {
            var sut = new DnsConnectSpy((_, ct) =>
                Task.Run<IPAddress[]>(() =>
                {
                    ct.WaitHandle.WaitOne();
                    ct.ThrowIfCancellationRequested();
                    return Array.Empty<IPAddress>();
                }, ct))
            {
                MpiStatic = new MPIStatic
                {
                    Address = "example.com",
                    EndPointType = "dns",
                    Timeout = 0
                }
            };

            await sut.Connect();

            Assert.False(sut.MpiConnect.IsUp);
            Assert.Equal("Exception", sut.MpiConnect.PingInfo.Status);
            Assert.Contains("Timeout while resolving host address", sut.MpiConnect.Message);
        }
    }
}
