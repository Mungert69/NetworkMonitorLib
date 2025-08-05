using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

/* ───── stub that overrides the RunCommandAsync seam ───── */
internal sealed class QuantumStub : QuantumConnect
{
    private readonly string _fake;

    public QuantumStub(
        IList<AlgorithmInfo> algos,
        string               fakeOutput,
        ILogger              log)
        : base(new List<AlgorithmInfo>(algos), "/oqs", "/bin", log)
        => _fake = fakeOutput;

    protected override Task<string> RunCommandAsync(
        string oqs, string curve, string addr, int port, bool addEnv, CancellationToken _)
        => Task.FromResult(_fake);
}

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class QuantumConnectTests
    {
        private static AlgorithmInfo Algo(string n,int id=1,bool env=false,bool en=true) =>
            new() { AlgorithmName=n, DefaultID=id, AddEnv=env, Enabled=en, EnvironmentVariable="VAR" };

        private static ILogger Log() => new Mock<ILogger>().Object;

        [Fact]
        public async Task Connect_sets_failure_status_when_no_qs_algorithm()
        {
            var qc = new QuantumStub(
                        new[]{ Algo("curve1") },
                        "Alert: handshake failure",
                        Log());

            qc.MpiStatic = new MPIStatic { Address="host", Port=443, Timeout=1000 };
            await qc.Connect();

            Assert.False(qc.MpiConnect.IsUp);
            Assert.Contains("Could not negotiate quantum safe handshake", qc.MpiConnect.Message);
        }

        [Fact]
        public async Task ProcessAlgorithm_returns_success_on_valid_keyshare()
        {
            var algo = Algo("curveQS", id:0x1234);

            // Minimal valid TL13 ServerHello with key_share 0x1234 (same pattern as your unit-test)
            string body = "0303" + new string('0',64) + "00" + "1301" + "00" + "0006" + "003300021234";
            string hex  = "0200002E" + body;          // handshake header + body
            var banner  = $"ServerHello\n{hex}\n";

            var qc  = new QuantumStub(new[]{algo}, banner, Log());
            var res = await qc.ProcessAlgorithm(algo, "host", 443);

            Assert.True(res.Success);
            Assert.Equal("curveQS", res.Data);        // matched by GroupID
        }

        [Fact]
        public async Task IsQuantumSafe_uses_legacy_when_modern_fails()
        {
            var modern = Algo("modern", 1);
            var legacy = Algo("legacy", 0x5678, env:true);

            string body = "0303" + new string('0',64) + "00" + "1301" + "00" + "0006" + "003300025678";
            string hex  = "0200002E" + body;
            var banner  = $"ServerHello\n{hex}\n";

            var qc  = new QuantumStub(new[]{modern, legacy}, banner, Log());
            var res = await qc.IsQuantumSafe("host", 443);

            Assert.True(res.Success);
            Assert.Equal("legacy", res.Data);
        }

        [Fact]
        public async Task ProcessBatchAlgorithms_no_algos_returns_failure()
        {
            var qc = new QuantumStub(new List<AlgorithmInfo>(), "", Log());

            var r = await qc.ProcessBatchAlgorithms(new List<AlgorithmInfo>(), "host", 443);

            Assert.False(r.Success);
            Assert.Contains("No algorithms", r.Message);
        }
    }
}
