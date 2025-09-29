using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class SocketConnectTests
    {
        [Fact]
        public async Task Connect_Succeeds_WhenEndpointAvailable()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync();

            var connect = new SocketConnect
            {
                MpiStatic = new MPIStatic
                {
                    Address = "127.0.0.1",
                    Port = (ushort)port,
                    Timeout = 2000,
                    EndPointType = "rawconnect"
                }
            };

            await connect.Connect();

            Assert.True(connect.MpiConnect.IsUp);
            Assert.Equal("Connected", connect.MpiConnect.PingInfo.Status);

            var client = await acceptTask;
            client.Dispose();
            listener.Stop();
        }

        [Fact]
        public async Task Connect_UnresolvableDomain_ReportsFailure()
        {
            var connect = new SocketConnect
            {
                MpiStatic = new MPIStatic
                {
                    Address = "nonexistent-domain.invalid",
                    Port = 0,
                    Timeout = 500,
                    EndPointType = "rawconnect"
                }
            };

            await connect.Connect();

            Assert.False(connect.MpiConnect.IsUp);
            Assert.Equal("Exception", connect.MpiConnect.PingInfo.Status);
            Assert.Contains("Failed to connect", connect.MpiConnect.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
