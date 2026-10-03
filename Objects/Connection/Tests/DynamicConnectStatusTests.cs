using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.CodeAnalysis.CSharp;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection;

public class DynamicConnectStatusTests
{
    private static NetConnectConfig Config() => new(new ConfigurationBuilder().Build(), "", "");
    private static ConnectCompiler Compiler() => new(NullLoggerFactory.Instance,
        Config(), Mock.Of<IRabbitRepo>(), null, null);

    private static string Source(string declaration, string body, string construction = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.Extensions.Logging;
        using NetworkMonitor.Objects;
        namespace NetworkMonitor.Connection {
          public class GeneratedConnect : NetConnect {
            {{declaration}}
            {{construction}}
            public override Task Connect() { PreConnect(); {{body}} PostConnect(); return Task.CompletedTask; }
          }
        }
        """;

    private const string Labels = "public override IReadOnlyCollection<string> StatusLabels => new[] { \"Available\", \"Unavailable\", \"Exception\" };";

    [Theory]
    [InlineData("ProcessStatus(\"Available\", 123, \"Port 443 open\");", "Available", true, 123)]
    [InlineData("ProcessStatus(\"Port 443 open\", 123, \"Detail\");", "Invalid connect status", true, 123)]
    [InlineData("ProcessException(\"Remote diagnostic 123\", \"Unavailable\");", "Unavailable", false, 65535)]
    [InlineData("ProcessException(\"Remote diagnostic 123\", \"Error 123\");", "Invalid connect status", false, 65535)]
    [InlineData("MpiConnect.PingInfo.Status=\"Reading 13.67V\"; MpiConnect.PingInfo.RoundTripTime=1367; MpiConnect.Message=\"Battery detail\"; MpiConnect.IsUp=true;", "Invalid connect status", true, 1367)]
    [InlineData("MpiConnect.PingInfo.Status=null; MpiConnect.PingInfo.RoundTripTime=12; MpiConnect.Message=\"Missing label\";", "Invalid connect status", false, 12)]
    [InlineData("ProcessStatus(\"available\", 1, \"Case matters\");", "Invalid connect status", true, 1)]
    public async Task CompilerAndExecution_EnforceStatusWithoutChangingSampleOrOutcome(
        string body, string expected, bool up, int sample)
    {
        var compiler = Compiler();
        var instance = compiler.CreateConnectInstance(compiler.CompileAndGetType(Source(Labels, body), "NetworkMonitor.Connection.GeneratedConnect"));
        instance.MpiStatic.EndPointType = "generated";
        instance.MpiStatic.Timeout = 1000;
        await instance.Connect();
        Assert.Equal(expected, instance.MpiConnect.PingInfo.Status);
        Assert.Equal(up, instance.MpiConnect.IsUp);
        Assert.Equal((ushort)sample, instance.MpiConnect.PingInfo.RoundTripTime);
        Assert.NotEmpty(instance.MpiConnect.Message);
        if (expected == DynamicConnectStatusPolicy.InvalidStatus)
            Assert.Contains("Rejected undeclared connect status", instance.MpiConnect.Message);
        if (body.Contains("13.67V")) {
            Assert.Contains("13.67V", instance.MpiConnect.Message);
            Assert.Contains("Battery detail", instance.MpiConnect.Message);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("public override IReadOnlyCollection<string> StatusLabels => Array.Empty<string>();")]
    [InlineData("public override IReadOnlyCollection<string> StatusLabels => new[] { MpiStatic.Address };")]
    [InlineData("public override IReadOnlyCollection<string> StatusLabels => new[] { \"OK\", \"OK\" };")]
    [InlineData("public override IReadOnlyCollection<string> StatusLabels => new[] { \"\" };")]
    [InlineData("public override IReadOnlyCollection<string> StatusLabels => new[] { \" OK\" };")]
    [InlineData("public override IReadOnlyCollection<string> StatusLabels => new[] { \"OK\\n\" };")]
    public void InvalidDeclarations_AreRejectedWithActionableErrors(string declaration)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Compiler().CompileAndGetType(Source(declaration, ""), "NetworkMonitor.Connection.GeneratedConnect"));
        Assert.Contains("StatusLabels", error.Message);
    }

    [Fact]
    public void DeclarationLimits_AreEnforced()
    {
        string ArrayOf(IEnumerable<string> labels) => "public override IReadOnlyCollection<string> StatusLabels => new[] { " +
            string.Join(",", labels.Select(l => SyntaxFactory.Literal(l).ToFullString())) + " };";
        Compiler().CompileAndGetType(Source(ArrayOf(Enumerable.Range(1,64).Select(i => "Label " + i)), ""), "NetworkMonitor.Connection.GeneratedConnect");
        Assert.Throws<InvalidOperationException>(() => Compiler().CompileAndGetType(Source(ArrayOf(Enumerable.Range(1,65).Select(i => "Label " + i)), ""), "NetworkMonitor.Connection.GeneratedConnect"));
        Assert.Throws<InvalidOperationException>(() => Compiler().CompileAndGetType(Source(ArrayOf(new[] { new string('x',129) }), ""), "NetworkMonitor.Connection.GeneratedConnect"));
    }

    [Theory]
    [InlineData("public GeneratedConnect(NetConnectConfig cfg) {}")]
    [InlineData("public static INetConnect Create(ILogger logger, NetConnectConfig cfg) => new GeneratedConnect();")]
    [InlineData("public static INetConnect Create(ILogger logger, NetConnectConfig cfg, ICmdProcessorProvider cmd, IBrowserHost browser) => new GeneratedConnect();")]
    public async Task AlternateConstructionPaths_StillUseFinalGuard(string construction)
    {
        var compiler = Compiler();
        var instance = compiler.CreateConnectInstance(compiler.CompileAndGetType(
            Source(Labels, "MpiConnect.PingInfo.Status=\"Port 123 open\";", construction), "NetworkMonitor.Connection.GeneratedConnect"));
        await instance.Connect();
        Assert.Equal(DynamicConnectStatusPolicy.InvalidStatus, instance.MpiConnect.PingInfo.Status);
    }

    [Fact]
    public async Task FinalGuard_RunsWhenGeneratedConnectThrows()
    {
        var compiler = Compiler();
        var instance = compiler.CreateConnectInstance(compiler.CompileAndGetType(
            Source(Labels, "MpiConnect.PingInfo.Status=\"Error with address\"; throw new InvalidOperationException(\"failed\");"), "NetworkMonitor.Connection.GeneratedConnect"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => instance.Connect());
        Assert.Equal(DynamicConnectStatusPolicy.InvalidStatus, instance.MpiConnect.PingInfo.Status);
        instance.Cts.Dispose();
    }

    [Fact]
    public void Vocabulary_IsCopiedRatherThanBorrowed()
    {
        var labels = new List<string> { "Available" };
        var policy = new DynamicConnectStatusPolicy(labels, NullLogger.Instance);
        labels.Add("Reading 123");
        var result = new MPIConnect { PingInfo = new PingInfo { Status = "Reading 123", RoundTripTime = 123 }, IsUp = true, Message = "original detail" };
        policy.Validate(result);
        Assert.Equal(DynamicConnectStatusPolicy.InvalidStatus, result.PingInfo.Status);
        Assert.Contains("original detail", result.Message);
        Assert.True(result.IsUp);
        Assert.Equal((ushort)123, result.PingInfo.RoundTripTime);
    }

    [Fact]
    public void Factory_CannotSubstituteAnUnvalidatedType()
    {
        var compiler = Compiler();
        var source = Source(Labels, "", "public static INetConnect Create(ILogger logger, NetConnectConfig cfg) => new SMTPConnect();");
        var type = compiler.CompileAndGetType(source, "NetworkMonitor.Connection.GeneratedConnect");
        var error = Assert.Throws<InvalidOperationException>(() => compiler.CreateConnectInstance(type));
        Assert.Contains("declared NetConnect class", error.Message);
    }

    [Fact]
    public async Task AddConnect_InvalidDeclaration_DoesNotRegister()
    {
        var provider = new ConnectProvider(NullLoggerFactory.Instance, Mock.Of<IRabbitRepo>(), Config(), null, null);
        var request = new ProcessorScanDataObj { Type = "Generated", Arguments = Source("", ""), CallingService = "test" };
        var result = await provider.AddConnect(request);
        Assert.False(result.Success);
        Assert.Contains("StatusLabels", result.Message);
        Assert.DoesNotContain("Generated", provider.ConnectTypes);
    }
}
