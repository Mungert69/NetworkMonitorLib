using System;
using System.Reflection;
using Xunit;
using NetworkMonitor.Connection;

namespace NetworkMonitor.Connection.CommandProcessors.Tests
{
    public class BleBroadcastCmdProcessorTests
    {
        private static MethodInfo GetPrivateStatic(string name)
        {
            var method = typeof(BleBroadcastCmdProcessor).GetMethod(
                name,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return method!;
        }

        [Fact]
        public void IsVictronInstantReadout_AcceptsDirectExtraRecord()
        {
            var payload = Convert.FromHexString("E10201C692A03B9F81B70B000000C20D7C1B");
            var method = GetPrivateStatic("IsVictronInstantReadout");

            var result = (bool)method.Invoke(null, new object[] { payload, "manufacturer", (byte)0xA0 })!;

            Assert.True(result);
        }

        [Fact]
        public void IsVictronInstantReadout_AcceptsProductAdvert()
        {
            var payload = Convert.FromHexString("E102100274A001A3584C6C29E6945EE800353DF9FC3B");
            var method = GetPrivateStatic("IsVictronInstantReadout");

            var result = (bool)method.Invoke(null, new object[] { payload, "manufacturer", (byte)0x4C })!;

            Assert.True(result);
        }

        [Fact]
        public void IsVictronInstantReadout_RejectsVeSmartPacket()
        {
            var payload = Convert.FromHexString("E10202C692A03B9F99DA0E000E37662622E6047EA056F42759FB699E");
            var method = GetPrivateStatic("IsVictronInstantReadout");

            var result = (bool)method.Invoke(null, new object[] { payload, "manufacturer", (byte)0xA0 })!;

            Assert.False(result);
        }

        [Fact]
        public void IsVictronInstantReadout_ParsesRawScanRecord()
        {
            var manufacturer = Convert.FromHexString("E10201C692A03B9F");
            var raw = new byte[manufacturer.Length + 2];
            raw[0] = (byte)(manufacturer.Length + 1);
            raw[1] = 0xFF;
            Buffer.BlockCopy(manufacturer, 0, raw, 2, manufacturer.Length);

            var method = GetPrivateStatic("IsVictronInstantReadout");
            var result = (bool)method.Invoke(null, new object[] { raw, "raw", (byte)0xA0 })!;

            Assert.True(result);
        }

        [Fact]
        public void TryExtractVictronRecord_DirectExtraRecord()
        {
            var payload = Convert.FromHexString("E10201C692A03B9F81B70B000000C20D7C1B");
            var method = GetPrivateStatic("TryExtractVictronRecord");

            var args = new object?[] { payload, "manufacturer", null, null };
            var ok = (bool)method.Invoke(null, args)!;

            Assert.True(ok);
            var record = args[2]!;
            var recordType = (byte)record.GetType().GetField("RecordType")!.GetValue(record)!;
            var nonce = (ushort)record.GetType().GetField("Nonce")!.GetValue(record)!;
            var keyCheck = (byte)record.GetType().GetField("KeyCheck")!.GetValue(record)!;

            Assert.Equal(0x01, recordType);
            Assert.Equal((ushort)0x92C6, nonce);
            Assert.Equal(0xA0, keyCheck);
        }

        [Fact]
        public void TryExtractVictronRecord_ProductAdvert()
        {
            var payload = Convert.FromHexString("E102100274A001A3584C6C29E6945EE800353DF9FC3B");
            var method = GetPrivateStatic("TryExtractVictronRecord");

            var args = new object?[] { payload, "manufacturer", null, null };
            var ok = (bool)method.Invoke(null, args)!;

            Assert.True(ok);
            var record = args[2]!;
            var recordType = (byte)record.GetType().GetField("RecordType")!.GetValue(record)!;
            var nonce = (ushort)record.GetType().GetField("Nonce")!.GetValue(record)!;
            var keyCheck = (byte)record.GetType().GetField("KeyCheck")!.GetValue(record)!;

            Assert.Equal(0x01, recordType);
            Assert.Equal((ushort)0x58A3, nonce);
            Assert.Equal(0x4C, keyCheck);
        }
    }
}
