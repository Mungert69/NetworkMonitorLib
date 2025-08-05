using System;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    // Concrete implementation for testing
    public class TestNetConnect : NetConnect
    {
        public override Task Connect() => Task.CompletedTask;
        public void CallSetSiteHash(string hash) => SetSiteHash(hash);
        public void CallProcessException(string message, string shortMessage) => ProcessException(message, shortMessage);
        public void CallProcessStatus(string reply, ushort timeTaken, string extraData = "") => ProcessStatus(reply, timeTaken, extraData);
    }

    public class NetConnectTests
    {
        [Fact]
        public void Property_GettersAndSetters_Work()
        {
            var netConnect = new TestNetConnect();
            netConnect.RoundTrip = 123;
            netConnect.PiID = 42;
            netConnect.IsLongRunning = true;
            netConnect.IsRunning = true;
            netConnect.IsQueued = true;
            netConnect.IsEnabled = false;
            var cts = new CancellationTokenSource();
            netConnect.Cts = cts;
            var mpiConnect = new MPIConnect();
            netConnect.MpiConnect = mpiConnect;
            var mpiStatic = new MPIStatic();
            netConnect.MpiStatic = mpiStatic;

            Assert.Equal((ushort)123, netConnect.RoundTrip);
            Assert.Equal((uint)42, netConnect.PiID);
            Assert.True(netConnect.IsLongRunning);
            Assert.True(netConnect.IsRunning);
            Assert.True(netConnect.IsQueued);
            Assert.False(netConnect.IsEnabled);
            Assert.Equal(cts, netConnect.Cts);
            Assert.Equal(mpiConnect, netConnect.MpiConnect);
            Assert.Equal(mpiStatic, netConnect.MpiStatic);
        }

        [Fact]
        public void PreConnect_InitializesFields()
        {
            var netConnect = new TestNetConnect();
            netConnect.PiID = 99;
            netConnect.MpiStatic = new MPIStatic
            {
                MonitorIPID = 123,
                Timeout = 100,
                SiteHash = "abc"
            };

            netConnect.PreConnect();

            Assert.True(netConnect.IsRunning);
            Assert.NotNull(netConnect.MpiConnect.PingInfo);
            Assert.Equal((uint)99, netConnect.MpiConnect.PingInfo.ID);
            Assert.Equal(123, netConnect.MpiConnect.PingInfo.MonitorPingInfoID);
            Assert.Equal("abc", netConnect.MpiConnect.SiteHash);
        }

        [Fact]
        public void PreConnect_ExtendTimeout_Works()
        {
            var netConnect = new TestNetConnect();
            netConnect.MpiStatic = new MPIStatic { Timeout = 10, SiteHash = "abc" };
            // Use reflection to set protected property
            var extendTimeoutProp = typeof(NetConnect).GetProperty("ExtendTimeout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(extendTimeoutProp);
            extendTimeoutProp.SetValue(netConnect, true);
            var extendTimeoutMultiplierProp = typeof(NetConnect).GetProperty("ExtendTimeoutMultiplier", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(extendTimeoutMultiplierProp);
            extendTimeoutMultiplierProp.SetValue(netConnect, 5);

            netConnect.PreConnect();

            // Should not throw, and Cts should be set
            Assert.NotNull(netConnect.Cts);
        }

        [Fact]
        public void PostConnect_SetsIsRunningFalse_AndDisposesCts()
        {
            var netConnect = new TestNetConnect();
            var cts = new CancellationTokenSource();
            netConnect.Cts = cts;
            netConnect.IsRunning = true;

            netConnect.PostConnect();

            Assert.False(netConnect.IsRunning);
            Assert.Throws<ObjectDisposedException>(() => cts.Token.ThrowIfCancellationRequested());
        }

        [Fact]
        public void SetSiteHash_SetsHash()
        {
            var netConnect = new TestNetConnect();
            netConnect.CallSetSiteHash("myhash");
            Assert.Equal("myhash", netConnect.MpiConnect.SiteHash);
        }

        [Fact]
        public void ProcessException_SetsFields()
        {
            var netConnect = new TestNetConnect();
            netConnect.MpiStatic = new MPIStatic { EndPointType = "http" };
            netConnect.MpiConnect.PingInfo = new PingInfo();

            netConnect.CallProcessException("Error (details)", "Short");

            Assert.Contains("HTTP: Failed to connect: Error", netConnect.MpiConnect.Message);
            Assert.False(netConnect.MpiConnect.IsUp);
            Assert.Equal("Short", netConnect.MpiConnect.PingInfo.Status);
            Assert.Equal(UInt16.MaxValue, netConnect.MpiConnect.PingInfo.RoundTripTime);
        }

        [Fact]
        public void ProcessStatus_SetsFields()
        {
            var netConnect = new TestNetConnect();
            netConnect.MpiConnect.PingInfo = new PingInfo();

            netConnect.CallProcessStatus("OK", 123, "extra");

            Assert.Equal("OK extra", netConnect.MpiConnect.Message);
            Assert.Equal("OK", netConnect.MpiConnect.PingInfo.Status);
            Assert.Equal((ushort)123, netConnect.MpiConnect.PingInfo.RoundTripTime);
            Assert.True(netConnect.MpiConnect.IsUp);
        }

        [Fact]
        public void ProcessStatus_WithoutExtraData_SetsFields()
        {
            var netConnect = new TestNetConnect();
            netConnect.MpiConnect.PingInfo = new PingInfo();

            netConnect.CallProcessStatus("OK", 123);

            Assert.Equal("OK", netConnect.MpiConnect.Message);
        }
    }
}
