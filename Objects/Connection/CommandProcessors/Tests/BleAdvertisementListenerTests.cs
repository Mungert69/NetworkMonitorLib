using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests;

public class BleAdvertisementListenerTests
{
    private const string Address = "AA:BB:CC:DD:EE:FF";
    private sealed class Source : IBleAdvertisementSource
    {
        private Action<string, byte[]>? _receive;
        public int Starts { get; private set; }
        public int Disposals { get; private set; }
        public void Start(Action<string, byte[]> receive, Action<string> failed) { Starts++; _receive = receive; }
        public void Emit(string address, byte[] bytes) => _receive!(address, bytes);
        public Action<string, byte[]> CurrentCallback => _receive!;
        public void Stop() { }
        public void Dispose() { Disposals++; }
    }
    private static byte[] Temperature(short centidegrees) => new byte[]
        { 7, 0x16, 0xd2, 0xfc, 0x40, 2, (byte)(centidegrees & 0xff), (byte)(centidegrees >> 8) };
    private static BleBroadcastCmdProcessor Processor() => new(NullLogger.Instance,
        new LocalCmdProcessorStates("ble", "BLE") { IsCmdAvailable = true }, Mock.Of<IRabbitRepo>(),
        new NetConnectConfig(new ConfigurationBuilder().Build(), "TestSection"));
    private static BleBroadcastListenCmdProcessor ListenProcessor() => new(NullLogger.Instance,
        new LocalCmdProcessorStates("ble", "BLE") { IsCmdAvailable = true }, Mock.Of<IRabbitRepo>(),
        new NetConnectConfig(new ConfigurationBuilder().Build(), "TestSection"));

    private sealed class Participant : NetConnect, IBleCycleParticipant
    {
        public BleCaptureRequirement? CaptureRequirement { get; set; }
        public BleAdvertisementSnapshot? Prepared { get; private set; }
        public void PrepareSnapshot(BleAdvertisementSnapshot snapshot) => Prepared = snapshot;
        public override Task Connect() => Task.CompletedTask;
    }

    [Fact]
    public void CoordinatorUsesCapabilitiesAndPreservesAnInflightSnapshot()
    {
        var source = new Source();
        using var listener = new BleAdvertisementListener(source);
        var coordinator = new BleCycleCoordinator(listener);
        var connect = new Participant
        {
            CaptureRequirement = new(Address, TimeSpan.FromSeconds(70)),
            MpiStatic = new MPIStatic { Address = Address, Enabled = true }
        };
        coordinator.BeginCycle(new[] { connect });
        source.Emit(Address, Temperature(1000));
        coordinator.CompleteCycle();
        Assert.True(coordinator.TryPrepareConnect(connect));
        var prepared = connect.Prepared;
        var token = connect.Cts;
        connect.IsRunning = true;
        source.Emit(Address, Temperature(2000));
        coordinator.CompleteCycle();
        Assert.False(coordinator.TryPrepareConnect(connect));
        Assert.Same(prepared, connect.Prepared);
        Assert.Same(token, connect.Cts);
        Assert.Single(prepared!.Packets);
        connect.IsRunning = false;
        connect.IsQueued = true;
        Assert.False(coordinator.TryPrepareConnect(connect));
        Assert.Same(prepared, connect.Prepared);
        connect.IsQueued = false;
        Assert.True(coordinator.TryPrepareConnect(connect));
        Assert.Equal(2, connect.Prepared!.Packets.Count);
        // Unrelated connects retain their scheduling/token state, even when running.
        var normal = new Mock<INetConnect>();
        normal.SetupGet(c => c.IsRunning).Returns(true);
        Assert.True(coordinator.TryPrepareConnect(normal.Object));
        normal.VerifyNoOtherCalls();
    }

    [Fact]
    public void StopAllowsRestartWithoutDisposingTheInjectedServiceOrKeepingStalePackets()
    {
        var source = new Source();
        var listener = new BleAdvertisementListener(source);
        var coordinator = new BleCycleCoordinator(listener);
        var connect = new BleBroadcastListenConnect(null)
            { MpiStatic = new MPIStatic { Enabled = true } };
        coordinator.BeginCycle(new[] { connect });
        source.Emit(Address, Temperature(1000));
        coordinator.CompleteCycle();
        var outstanding = listener.Snapshot;
        var oldCallback = source.CurrentCallback;
        coordinator.Stop();
        source.Emit(Address, Temperature(2000)); // Late callback after stop is ignored.
        coordinator.CompleteCycle();
        Assert.Empty(listener.Snapshot.Packets);
        Assert.Single(outstanding.Packets);
        Assert.Equal(0, source.Disposals);
        coordinator.BeginCycle(new[] { connect });
        Assert.Equal(2, source.Starts);
        oldCallback(Address, Temperature(2000)); // Old registration cannot write into the restarted service.
        source.Emit(Address, Temperature(3000));
        coordinator.CompleteCycle();
        Assert.Single(listener.Snapshot.Packets);
        listener.Dispose(); // Only the composition root owns disposal.
        listener.Dispose();
        Assert.Equal(1, source.Disposals);
    }

    [Fact]
    public async Task ExistingStaticAndRuntimeCompiledProcessorsReceiveTheInjectedListener()
    {
        using var listener = new BleAdvertisementListener(new Source());
        var config = new NetConnectConfig(new ConfigurationBuilder().Build(), "TestSection")
            { CommandPath = "" };
        var provider = new CmdProcessorProvider(NullLoggerFactory.Instance, Mock.Of<IRabbitRepo>(),
            config, Mock.Of<IBrowserHost>(), listener);
        var compiler = (CmdProcessorCompiler)typeof(CmdProcessorProvider)
            .GetField("_compiler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(provider)!;
        // Runtime deployments supply these through CommandPath/dlls. Keep the
        // production compiler unchanged and supply host metadata for this test.
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).Select(a => a.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        typeof(CmdProcessorCompiler).GetField("_cachedReferences", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(compiler, paths.Select(path => (Microsoft.CodeAnalysis.MetadataReference)Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path)).ToList());
        compiler.HandleStaticProcessor("BleBroadcast");
        compiler.HandleStaticProcessor("BleBroadcastListen");
        compiler.HandleStaticProcessor("Ping");
        Assert.Same(listener, Assert.IsAssignableFrom<IBleAdvertisementListenerConsumer>(provider.GetProcessor("BleBroadcast")).Listener);
        Assert.Same(listener, Assert.IsAssignableFrom<IBleAdvertisementListenerConsumer>(provider.GetProcessor("BleBroadcastListen")).Listener);
        Assert.IsNotAssignableFrom<IBleAdvertisementListenerConsumer>(provider.GetProcessor("Ping"));
        const string source = """
            using Microsoft.Extensions.Logging;
            using NetworkMonitor.Objects;
            using NetworkMonitor.Objects.Repository;
            namespace NetworkMonitor.Connection;
            public class BleInjectedCmdProcessor : BleBroadcastCmdProcessor
            {
                public BleInjectedCmdProcessor(ILogger logger, ILocalCmdProcessorStates states,
                    IRabbitRepo repo, NetConnectConfig cfg) : base(logger, states, repo, cfg) { }
            }
            """;
        var result = await compiler.HandleDynamicProcessor("BleInjected", processorSourceCode: source);
        Assert.True(result.Success, result.Message);
        Assert.Same(listener, Assert.IsAssignableFrom<IBleAdvertisementListenerConsumer>(provider.GetProcessor("BleInjected")).Listener);
        // Compile the actual BLE processor source with its unchanged 4-argument constructor.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Objects/Connection/CommandProcessors/BleBroadcastCmdProcessor.cs"))) root = root.Parent;
        Assert.NotNull(root);
        foreach (string type in new[] { "BleBroadcast", "BleBroadcastListen" })
        {
            var actualSource = File.ReadAllText(Path.Combine(root!.FullName, "Objects/Connection/CommandProcessors", type + "CmdProcessor.cs"));
            result = await compiler.HandleDynamicProcessor(type, processorSourceCode: actualSource);
            Assert.True(result.Success, result.Message);
            Assert.Same(listener, Assert.IsAssignableFrom<IBleAdvertisementListenerConsumer>(provider.GetProcessor(type)).Listener);
        }

    }

    [Fact]
    public void PublicationPrecedesEvictionAndOldSnapshotsStayImmutable()
    {
        long now = 0;
        var source = new Source();
        using var listener = new BleAdvertisementListener(source, () => now);
        listener.Configure(new[] { (Address, TimeSpan.FromSeconds(50)) }, true);
        var bytes = Temperature(1000);
        source.Emit(Address, bytes);
        source.Emit("11:22:33:44:55:66", bytes);
        bytes[0] = 0; // Caller cannot mutate stored packet bytes.
        Assert.Empty(listener.Snapshot.Packets); // No initial snapshot.
        listener.CompleteCycle();
        var first = listener.Snapshot;
        Assert.Equal(2, first.Packets.Count);
        Assert.Equal(7, first.Packets[0].CopyBytes()[0]);
        var copy = first.Packets[0].CopyBytes(); copy[0] = 0;
        now = 100 * Stopwatch.Frequency;
        listener.CompleteCycle();
        Assert.Single(listener.Snapshot.Packets); // Unprotected address was cleared.
        now++;
        listener.CompleteCycle(); // Snapshot includes the just-expired protected packet before eviction.
        listener.CompleteCycle();
        Assert.Empty(listener.Snapshot.Packets);
        Assert.Equal(2, first.Packets.Count); // Older async reader still owns its view.
    }

    [Fact]
    public void LongestWindowControlsRetentionAndConfigurationCanRemoveProtection()
    {
        long now = 0;
        var source = new Source();
        using var listener = new BleAdvertisementListener(source, () => now);
        listener.Configure(new[] { (Address, TimeSpan.FromHours(1)), ("aabbccddeeff", TimeSpan.FromHours(26)) }, true);
        source.Emit(Address, Temperature(1000));
        now = 51L * 3600 * Stopwatch.Frequency;
        listener.CompleteCycle(); listener.CompleteCycle();
        Assert.Single(listener.Snapshot.Packets);
        listener.Configure(Array.Empty<(string, TimeSpan)>(), true);
        listener.CompleteCycle(); listener.CompleteCycle();
        Assert.Empty(listener.Snapshot.Packets);
        Assert.Equal(1, source.Starts); // Multiple cycles use one scan registration.
    }

    [Fact]
    public async Task AveragesSelectedPhysicalMetricBeforeEncodingAndKeepsLatestText()
    {
        long now = 0;
        var source = new Source();
        using var listener = new BleAdvertisementListener(source, () => now);
        listener.Configure(new[] { (Address, TimeSpan.FromSeconds(50)) }, true);
        source.Emit(Address, Temperature(-1000));
        now = 10 * Stopwatch.Frequency;
        source.Emit(Address, Temperature(3000));
        source.Emit(Address, Convert.FromHexString("0516D2FC4002")); // Decode fails; keep valid samples.
        listener.CompleteCycle();
        using var processor = Processor();
        var provider = new Mock<ICmdProcessorProvider>();
        provider.Setup(p => p.GetProcessor("BleBroadcast")).Returns(processor);
        var connect = new BleBroadcastConnect(provider.Object)
        {
            MpiStatic = new MPIStatic { Address = Address, Args = "--format bthome --metric temperature", Timeout = 5000 },
            CycleSnapshot = listener.Snapshot
        };
        await connect.Connect();
        Assert.False(connect.IsLongRunning);
        Assert.True(connect.MpiConnect.IsUp, connect.MpiConnect.Message);
        var encoding = BleMetricCatalogue.Find("bthome", "temperature")!;
        Assert.True(encoding.TryEncode(10, out var expected));
        Assert.Equal(expected, connect.MpiConnect.PingInfo.RoundTripTime);
        Assert.Contains("Temperature: 30 °C", connect.MpiConnect.Message);
        Assert.DoesNotContain("Temperature: 10 °C", connect.MpiConnect.Message);
    }

    [Fact]
    public void WindowUsesSnapshotTimeRatherThanDelayedExecutionTime()
    {
        long now = 0;
        var source = new Source();
        using var listener = new BleAdvertisementListener(source, () => now);
        listener.Configure(new[] { (Address, TimeSpan.FromSeconds(50)) }, true);
        source.Emit(Address, Temperature(1000));
        now = 60 * Stopwatch.Frequency;
        source.Emit(Address, Temperature(3000));
        listener.CompleteCycle();
        var snapshot = listener.Snapshot;
        now = 600 * Stopwatch.Frequency;
        listener.CompleteCycle(); listener.CompleteCycle();
        using var processor = Processor();
        var result = processor.ReadSnapshot($"--address {Address} --format bthome --metric temperature", snapshot,
            TimeSpan.FromSeconds(50), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(30d, Assert.IsType<BleDecodedPayload>(result.Data).Readings.Single(r => r.Metric == "temperature").Value);
    }

    [Fact]
    public void RawListenIncludesAllPacketsWithoutDecodingOrCaptureLimitAndSkipsPreviousSequences()
    {
        var source = new Source();
        using var listener = new BleAdvertisementListener(source);
        listener.Configure(new[] { (Address, TimeSpan.FromSeconds(50)) }, true);
        for (int i = 0; i < 25; i++) source.Emit(Address, Temperature(1000));
        listener.CompleteCycle();
        using var processor = ListenProcessor();
        var first = processor.ReadSnapshot("--format bthome --max_captures 1", listener.Snapshot, 0, CancellationToken.None);
        Assert.True(first.Success);
        Assert.Contains("25 advertisement(s)", first.Message);
        Assert.Contains("0716D2FC4002E803", first.Message);
        Assert.DoesNotContain("Temperature:", first.Message);
        long previous = listener.Snapshot.LastSequence;
        source.Emit(Address, Temperature(2000));
        listener.CompleteCycle();
        var next = processor.ReadSnapshot("", listener.Snapshot, previous, CancellationToken.None);
        Assert.Contains("1 advertisement(s)", next.Message);
    }

    [Fact]
    public void TargetedReadersChooseTheirOwnMetricAndLeaveOtherReadingsAtLatestValues()
    {
        var source = new Source();
        using var listener = new BleAdvertisementListener(source);
        listener.Configure(new[] { (Address, TimeSpan.FromSeconds(50)) }, true);
        source.Emit(Address, Convert.FromHexString("0A16D2FC4002E80303E803")); // 10 C, 10 %
        source.Emit(Address, Convert.FromHexString("0A16D2FC4002B80B032823")); // 30 C, 90 %
        listener.CompleteCycle();
        using var processor = Processor();
        var temperature = processor.ReadSnapshot($"--address {Address} --format bthome --metric temperature",
            listener.Snapshot, TimeSpan.FromSeconds(50), CancellationToken.None);
        var humidity = processor.ReadSnapshot($"--address {Address} --format bthome --metric humidity",
            listener.Snapshot, TimeSpan.FromSeconds(50), CancellationToken.None);
        Assert.True(temperature.Success, temperature.Message);
        Assert.True(humidity.Success, humidity.Message);
        var readings = Assert.IsType<BleDecodedPayload>(temperature.Data).Readings;
        Assert.Equal(20d, readings.Single(r => r.Metric == "temperature").Value);
        Assert.Equal(90d, readings.Single(r => r.Metric == "humidity").Value);
        Assert.Equal(50d, Assert.IsType<BleDecodedPayload>(humidity.Data).Readings.Single(r => r.Metric == "humidity").Value);
    }

    [Fact]
    public void ManufacturerSelectionIgnoresUnrelatedBlocksAndEmptyWindowsFail()
    {
        var source = new Source();
        using var listener = new BleAdvertisementListener(source);
        listener.Configure(new[] { (Address, TimeSpan.FromSeconds(50)) }, true);
        var ruuvi = Convert.FromHexString("99040512FC5394C37C0004FFFC040CAC364200CDCBB8334C884F");
        source.Emit(Address, new byte[] { 3, 0xff, 0x4c, 0, (byte)(ruuvi.Length + 1), 0xff }.Concat(ruuvi).ToArray());
        listener.CompleteCycle();
        using var processor = Processor();
        var result = processor.ReadSnapshot($"--address {Address} --format ruuvi --metric temperature",
            listener.Snapshot, TimeSpan.FromSeconds(50), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal(24.3d, Assert.IsType<BleDecodedPayload>(result.Data).Readings.Single(r => r.Metric == "temperature").Value!.Value, 6);
        var empty = processor.ReadSnapshot("--address 11:22:33:44:55:66 --format ruuvi --metric temperature",
            listener.Snapshot, TimeSpan.FromSeconds(50), CancellationToken.None);
        Assert.False(empty.Success);
    }

    [Fact]
    public void CycleRulesRespectBothEnabledFlagsAndRefreshProtectionAfterAnEdit()
    {
        long now = 0;
        var source = new Source();
        using var listener = new BleAdvertisementListener(source, () => now);
        var connect = new BleBroadcastConnect(null)
        {
            MpiStatic = new MPIStatic { Address = Address, Timeout = 5000, Enabled = false }
        };
        var factory = new Mock<IConnectFactory>();
        var coordinator = new BleCycleCoordinator(listener);
        factory.Setup(f => f.GetNetConnectObj(It.IsAny<MonitorPingInfo>(), It.IsAny<PingParams>())).Returns(connect);
        var collection = new NetConnectCollection(NullLogger.Instance,
            new NetConnectConfig(new ConfigurationBuilder().Build(), "TestSection"), factory.Object);
        collection.Add(new MonitorPingInfo());
        coordinator.BeginCycle(collection.GetConfiguredConnects());
        Assert.Equal(0, source.Starts);
        connect.MpiStatic.Enabled = true;
        coordinator.BeginCycle(collection.GetConfiguredConnects());
        source.Emit(Address, Temperature(1000));
        coordinator.CompleteCycle();
        now = 90 * Stopwatch.Frequency;
        coordinator.CompleteCycle(); coordinator.CompleteCycle();
        Assert.Single(listener.Snapshot.Packets); // Protected for 2 * (5000ms * 10).
        connect.MpiStatic.Timeout = 1000;
        coordinator.BeginCycle(collection.GetConfiguredConnects());
        coordinator.CompleteCycle(); coordinator.CompleteCycle();
        Assert.Empty(listener.Snapshot.Packets);
        Assert.Equal(1, source.Starts);
    }

    [Fact]
    public async Task ConcurrentReceptionAndCycleCleanupDoNotLoseProtectedPackets()
    {
        var source = new Source();
        using var listener = new BleAdvertisementListener(source);
        listener.Configure(new[] { (Address, TimeSpan.FromDays(1)) }, true);
        await Task.WhenAll(Task.Run(() => { for (int i = 0; i < 1000; i++) source.Emit(Address, Temperature(1000)); }),
            Task.Run(() => { for (int i = 0; i < 50; i++) listener.CompleteCycle(); }));
        listener.CompleteCycle();
        Assert.Equal(1000, listener.Snapshot.Packets.Count);
        Assert.Equal(1000, listener.Snapshot.Packets.Select(p => p.Sequence).Distinct().Count());
    }
}
