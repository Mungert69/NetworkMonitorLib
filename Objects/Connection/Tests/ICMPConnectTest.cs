using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class ICMPConnectTestable : ICMPConnect
    {
        public void TestPingCompletedCallback(PingReply? reply = null, bool cancelled = false, Exception? error = null)
        {
            // Use reflection to create PingCompletedEventArgs and set private fields
            var ctor = typeof(PingCompletedEventArgs).GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new Type[] { typeof(PingReply), typeof(Exception), typeof(bool), typeof(object) },
                null
            );
            if (ctor == null)
                throw new InvalidOperationException("Could not find PingCompletedEventArgs constructor.");
            var e = (PingCompletedEventArgs)ctor.Invoke(new object?[] { reply, error, cancelled, null }!);
            var method = typeof(ICMPConnect).GetMethod("PingCompletedCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null)
                throw new InvalidOperationException("Could not find PingCompletedCallback method.");
            method.Invoke(this, new object?[] { this, e }!);
        }
        public void TestProcessPingStatus(PingReply? reply)
        {
            var method = typeof(ICMPConnect).GetMethod("ProcessPingStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null)
                throw new InvalidOperationException("Could not find ProcessPingStatus method.");
            method.Invoke(this, new object?[] { reply }!);
        }
    }

    public class ICMPConnectTests
    {
        // NOTE: It is not possible to create a custom PingReply with arbitrary status using reflection in .NET reliably.
        // So we skip tests that require a custom PingReply. The following tests cover the null/error/cancelled paths.

        // Duplicate removed

        [Fact]
        public void PingCompletedCallback_Cancelled_SetsPingReplyNull()
        {
            var icmp = new ICMPConnectTestable();
            icmp.MpiStatic = new MPIStatic { Address = "127.0.0.1", EndPointType = "icmp" };
            icmp.MpiConnect.PingInfo = new PingInfo();

            icmp.TestPingCompletedCallback(null, cancelled: true);

            Assert.NotNull(icmp.MpiConnect);
            Assert.NotNull(icmp.MpiConnect.PingInfo);
            Assert.False(icmp.MpiConnect.IsUp);
            Assert.Equal("Ping Reply Null", icmp.MpiConnect.PingInfo.Status);
            Assert.Contains("Ping Reply Null", icmp.MpiConnect.Message);
        }

        [Fact]
        public void PingCompletedCallback_Error_SetsPingReplyNull()
        {
            var icmp = new ICMPConnectTestable();
            icmp.MpiStatic = new MPIStatic { Address = "127.0.0.1", EndPointType = "icmp" };
            icmp.MpiConnect.PingInfo = new PingInfo();

            var ex = new InvalidOperationException("fail");
            icmp.TestPingCompletedCallback(null, error: ex);

            Assert.NotNull(icmp.MpiConnect);
            Assert.NotNull(icmp.MpiConnect.PingInfo);
            Assert.False(icmp.MpiConnect.IsUp);
            Assert.Equal("Ping Reply Null", icmp.MpiConnect.PingInfo.Status);
            Assert.Contains("Ping Reply Null", icmp.MpiConnect.Message);
        }

        [Fact]
        public void PingCompletedCallback_PingReplyNull_SetsException()
        {
            var icmp = new ICMPConnectTestable();
            icmp.MpiStatic = new MPIStatic { Address = "127.0.0.1", EndPointType = "icmp" };
            icmp.MpiConnect.PingInfo = new PingInfo();

            icmp.TestPingCompletedCallback(null);

            Assert.NotNull(icmp.MpiConnect);
            Assert.NotNull(icmp.MpiConnect.PingInfo);
            Assert.False(icmp.MpiConnect.IsUp);
            Assert.Equal("Ping Reply Null", icmp.MpiConnect.PingInfo.Status);
            Assert.Contains("Ping Reply Null", icmp.MpiConnect.Message);
        }

        // Removed duplicate/incorrect expectation tests for cancelled/error

        [Fact]
        public async Task Connect_ExceptionInSendAsync_SetsPingReplyNull()
        {
            var icmp = new ICMPConnect();
            icmp.MpiStatic = new MPIStatic { Address = "invalid", EndPointType = "icmp" };
            icmp.MpiConnect.PingInfo = new PingInfo();

            // This will throw in SendAsync and be caught
            await icmp.Connect();

            Assert.NotNull(icmp.MpiConnect);
            Assert.NotNull(icmp.MpiConnect.PingInfo);
            Assert.False(icmp.MpiConnect.IsUp);
            Assert.Equal("Ping Reply Null", icmp.MpiConnect.PingInfo.Status);
        }
    }
}
