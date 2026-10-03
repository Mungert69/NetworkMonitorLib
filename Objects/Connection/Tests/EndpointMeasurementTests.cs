using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Utils;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection;

public class EndpointMeasurementTests
{
    [Fact]
    public async Task Boot_rebuilds_metadata_from_saved_custom_source_without_second_definition_file()
    {
        var directory = Directory.CreateTempSubdirectory("nm-measurement-tests-");
        try
        {
            var config = new NetConnectConfig(new ConfigurationBuilder().Build(), "", "")
                { AuthKey = "test-auth-key", CommandPath = directory.FullName };
            await config.SetAppIDAsync("owner-agent");
            var boot = new TaskCompletionSource<ProcessorDataObj>(TaskCreationOptions.RunContinuationsAsynchronously);
            var repo = new Mock<IRabbitRepo>();
            repo.Setup(r => r.PublishJsonZWithIDAsync<ProcessorDataObj>("dataUpdateMonitorPingInfos",
                It.IsAny<ProcessorDataObj>(), "owner-agent", ""))
                .Callback<string, ProcessorDataObj, string, string>((_, obj, _, _) => boot.TrySetResult(obj))
                .ReturnsAsync("");
            var provider = new ConnectProvider(NullLoggerFactory.Instance, repo.Object, config);
            var added = await provider.AddConnect(new ProcessorScanDataObj { Type = "saved", Arguments = """
                using System.Collections.Generic;
                using System.Threading.Tasks;
                namespace NetworkMonitor.Connection {
                  public class savedConnect : NetConnect {
                    public override IReadOnlyCollection<string> StatusLabels => new[] { "Available", "Exception" };
                    public override string Unit => "W";
                    public override string Type => "power";
                    public override Task Connect() => Task.CompletedTask;
                  }
                }
                """ });
            Assert.True(added.Success, added.Message);
            Assert.Equal("savedConnect.cs", Path.GetFileName(Directory.GetFiles(directory.FullName).Single()));
            boot = new TaskCompletionSource<ProcessorDataObj>(TaskCreationOptions.RunContinuationsAsynchronously);
            var restarted = new ConnectProvider(NullLoggerFactory.Instance, repo.Object, config);
            await restarted.Setup();
            var publication = await boot.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var definition = publication.EndpointMeasurements!.Single(d => d.EndpointType == "saved");
            Assert.Equal("W", definition.Unit);
            Assert.Equal(1, definition.Scale);
            Assert.Equal("power", definition.Type);
            Assert.Empty(publication.MonitorPingInfos);
            Assert.Empty(publication.PingInfos);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task Provider_advertises_dynamic_definition_once_per_trigger_and_deletes_it_from_snapshot()
    {
        var config = new NetConnectConfig(new ConfigurationBuilder().Build(), "", "")
            { AuthKey = "test-auth-key", CommandPath = "" };
        await config.SetAppIDAsync("owner-agent");
        var publications = new List<ProcessorDataObj>();
        var repo = new Mock<IRabbitRepo>();
        repo.Setup(r => r.PublishJsonZWithIDAsync<ProcessorDataObj>("dataUpdateMonitorPingInfos",
                It.IsAny<ProcessorDataObj>(), "owner-agent", ""))
            .Callback<string, ProcessorDataObj, string, string>((_, obj, _, _) => publications.Add(obj))
            .ReturnsAsync("");
        var provider = new ConnectProvider(NullLoggerFactory.Instance, repo.Object, config);
        await provider.Setup();
        await provider.PublishMeasurementCatalogue(); // boot plus an init trigger
        Assert.Equal(2, publications.Count);
        Assert.All(publications, p => Assert.Empty(p.EndpointMeasurements!));

        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            namespace NetworkMonitor.Connection {
              public class voltageConnect : NetConnect {
                public override IReadOnlyCollection<string> StatusLabels => new[] { "Available", "Exception" };
                public override string Unit => "V";
                public override double Scale => 0.01;
                public override string Type => "voltage";
                public override Task Connect() { return Task.CompletedTask; }
              }
            }
            """;
        var result = await provider.AddConnect(new ProcessorScanDataObj { Type = "voltage", Arguments = source });
        Assert.True(result.Success, result.Message);
        Assert.Equal(3, publications.Count);
        var definition = Assert.Single(publications.Last().EndpointMeasurements!);
        Assert.Equal("voltage", definition.EndpointType);
        Assert.Equal("V", definition.Unit);
        Assert.Equal(0.01, definition.Scale);
        Assert.Equal("voltage", definition.Type);
        var serialized = JsonUtils.WriteJsonObjectToString(publications.Last());
        Assert.Contains("EndpointMeasurements", serialized);
        Assert.DoesNotContain("EndpointMeasurements", JsonUtils.WriteJsonObjectToString(new ProcessorDataObj()));

        result = await provider.DeleteConnect(new ProcessorScanDataObj { Type = "voltage" });
        Assert.True(result.Success, result.Message);
        Assert.Equal(4, publications.Count);
        Assert.Empty(publications.Last().EndpointMeasurements!);
    }

    [Fact]
    public async Task Failed_publication_is_not_retried_and_does_not_fail_connect_setup()
    {
        var config = new NetConnectConfig(new ConfigurationBuilder().Build(), "", "")
            { AuthKey = "test-auth-key", CommandPath = "" };
        await config.SetAppIDAsync("owner-agent");
        var repo = new Mock<IRabbitRepo>();
        repo.Setup(r => r.PublishJsonZWithIDAsync<ProcessorDataObj>(It.IsAny<string>(),
            It.IsAny<ProcessorDataObj>(), It.IsAny<string>(), "")).ThrowsAsync(new Exception("offline"));
        var provider = new ConnectProvider(NullLoggerFactory.Instance, repo.Object, config);
        await provider.PublishMeasurementCatalogue();
        repo.Verify(r => r.PublishJsonZWithIDAsync<ProcessorDataObj>(It.IsAny<string>(),
            It.IsAny<ProcessorDataObj>(), It.IsAny<string>(), ""), Times.Once);
        Assert.Null(provider.CreateConnect("missing"));
    }
}
