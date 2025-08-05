using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

/* ───── stub that overrides RunCommandAsync ───── */
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

        // Banner with sentinel so ServerHelloHelper stops before hex collection → no exceptions
        private const string SentinelBanner = "ServerHello\n<<< end\n";

        [Fact]
        public async Task Connect_sets_failure_status_when_no_qs_algorithm()
        {
            var qc = new QuantumStub(
                        new[]{ Algo("curve1") },
                        SentinelBanner,
                        Log());

            qc.MpiStatic = new MPIStatic { Address="host", Port=443, Timeout=1000 };
            await qc.Connect();

            Assert.False(qc.MpiConnect.IsUp);
            Assert.Contains("Could not negotiate quantum safe handshake",
                            qc.MpiConnect.Message);
        }

        [Fact]
        public async Task ProcessAlgorithm_returns_failure_without_keyshare()
        {
            var algo = Algo("curveX", 0xABCD);
            var qc   = new QuantumStub(new[]{algo}, SentinelBanner, Log());

            var res = await qc.ProcessAlgorithm(algo, "host", 443);

            Assert.False(res.Success);
            Assert.Null(res.Data);
        }

        [Fact]
        public async Task IsQuantumSafe_returns_failure_when_all_algos_fail()
        {
            var modern = Algo("modern", 1);
            var legacy = Algo("legacy", 2, env:true);

            var qc  = new QuantumStub(new[]{modern, legacy}, SentinelBanner, Log());
            var res = await qc.IsQuantumSafe("host", 443);

            Assert.False(res.Success);
            Assert.Equal("No quantum-safe algorithm negotiated", res.Message);
        }

        [Fact]
        public async Task ProcessBatchAlgorithms_no_algos_returns_failure()
        {
            var qc = new QuantumStub(new List<AlgorithmInfo>(), "", Log());

            var r = await qc.ProcessBatchAlgorithms(
                        new List<AlgorithmInfo>(), "host", 443);

            Assert.False(r.Success);
            Assert.Contains("No algorithms", r.Message);
        }
    }
}
