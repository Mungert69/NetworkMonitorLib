using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Connection;
using Xunit;

public class EndPointTypeFactoryTest
{
    private class DummyLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Fact]
    public void GetProcessingTimeEstimate_ReturnsExpectedValues()
    {
        Assert.Equal("15-30 minutes (comprehensive network scans take longer)", EndPointTypeFactory.GetProcessingTimeEstimate("nmap"));
        Assert.Equal("30-60 minutes (website crawling is resource intensive)", EndPointTypeFactory.GetProcessingTimeEstimate("crawlsite"));
        Assert.Equal("One day (daily only runs once a day)", EndPointTypeFactory.GetProcessingTimeEstimate("dailycrawl"));
        Assert.Equal("5-10 minutes", EndPointTypeFactory.GetProcessingTimeEstimate("smtp"));
        Assert.Equal("5-10 minutes", EndPointTypeFactory.GetProcessingTimeEstimate("quantum"));
        Assert.Equal("5-10 minutes", EndPointTypeFactory.GetProcessingTimeEstimate("quantumcert"));
        Assert.Equal("2-5 minutes", EndPointTypeFactory.GetProcessingTimeEstimate("http"));
        Assert.Equal("2-5 minutes", EndPointTypeFactory.GetProcessingTimeEstimate("https"));
        Assert.Equal("1-2 minutes", EndPointTypeFactory.GetProcessingTimeEstimate("ping"));
        Assert.Equal("5-20 seconds (depends on broadcast interval)", EndPointTypeFactory.GetProcessingTimeEstimate("blebroadcast"));
        Assert.Equal("5-20 seconds (depends on broadcast interval)", EndPointTypeFactory.GetProcessingTimeEstimate("blebroadcastlisten"));
        Assert.Equal("2-10 minutes", EndPointTypeFactory.GetProcessingTimeEstimate("sitehash"));
        Assert.Equal("2-10 minutes", EndPointTypeFactory.GetProcessingTimeEstimate("unknown"));
        Assert.Equal("2-10 minutes", EndPointTypeFactory.GetProcessingTimeEstimate(null));
    }

    [Fact]
    public void GetEndpointTypes_ReturnsAllTypes()
    {
        var types = EndPointTypeFactory.GetEndpointTypes();
        Assert.Contains(types, t => t.InternalType == "icmp");
        Assert.Contains(types, t => t.InternalType == "http");
        Assert.Contains(types, t => t.InternalType == "smtp");
        Assert.Contains(types, t => t.InternalType == "nmap");
        Assert.Contains(types, t => t.InternalType == "crawlsite");
        Assert.Contains(types, t => t.InternalType == "dailycrawl");
        Assert.Contains(types, t => t.InternalType == "sitehash");
        Assert.Contains(types, t => t.InternalType == "dailyhugkeepalive");
        Assert.Contains(types, t => t.InternalType == "quantumcert");
        Assert.Contains(types, t => t.InternalType == "blebroadcast");
        Assert.Contains(types, t => t.InternalType == "blebroadcastlisten");
    }

    [Fact]
    public void GetInternalTypes_And_GetFriendlyNames_Work()
    {
        var internalTypes = EndPointTypeFactory.GetInternalTypes();
        var friendlyNames = EndPointTypeFactory.GetFriendlyNames();
        Assert.Contains("icmp", internalTypes);
        Assert.Contains("ICMP (Simple Ping)", friendlyNames);
    }

    [Fact]
    public void GetFriendlyName_And_GetInternalType_WorkCorrectly()
    {
        Assert.Equal("ICMP (Simple Ping)", EndPointTypeFactory.GetFriendlyName("icmp"));
        Assert.Equal("icmp", EndPointTypeFactory.GetInternalType("ICMP (Simple Ping)"));
        Assert.Equal("Unknown", EndPointTypeFactory.GetFriendlyName("notatype"));
        Assert.Throws<ArgumentException>(() => EndPointTypeFactory.GetInternalType("notafriendlyname"));
    }

    [Fact]
    public void GetEndpointType_And_GetEndpointTypeByName_WorkCorrectly()
    {
        var et = EndPointTypeFactory.GetEndpointType("icmp");
        Assert.Equal("icmp", et.InternalType);
        var et2 = EndPointTypeFactory.GetEndpointTypeByName("ICMP (Simple Ping)");
        Assert.Equal("icmp", et2.InternalType);
        Assert.Throws<ArgumentException>(() => EndPointTypeFactory.GetEndpointType("notatype"));
        Assert.Throws<ArgumentException>(() => EndPointTypeFactory.GetEndpointTypeByName("notafriendlyname"));
    }

    [Fact]
    public void GetEnabledEndPoints_ExcludesDisabled()
    {
        var all = EndPointTypeFactory.GetInternalTypes();
        var disabled = new List<string> { "icmp", "http" };
        var enabled = EndPointTypeFactory.GetEnabledEndPoints(disabled);
        Assert.DoesNotContain("icmp", enabled);
        Assert.DoesNotContain("http", enabled);
        Assert.All(enabled, t => Assert.DoesNotContain(t, disabled, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResponseTimeThresholds_ReturnsCorrectThresholds()
    {
        foreach (var endpointType in EndPointTypeFactory.GetInternalTypes())
        {
            var thresholds = EndPointTypeFactory.ResponseTimeThresholds.GetValueOrDefault(endpointType);
            if (thresholds != null)
            {
                Assert.True(thresholds.AllPorts.Excellent >= 0);
                Assert.True(thresholds.SpecificPort.Excellent >= 0);
            }
        }
    }

    [Fact]
    public void CreateNetConnect_ReturnsCorrectType()
    {
        var logger = new DummyLogger();
        var httpClient = new HttpClient();
        var httpsClient = new HttpClient();
        var algoList = new List<NetworkMonitor.Objects.AlgorithmInfo>();
        string oqsProviderPath = "";
        string commandPath = "";

        Assert.IsType<HTTPConnect>(EndPointTypeFactory.CreateNetConnect("http", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<HTTPConnect>(EndPointTypeFactory.CreateNetConnect("https", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<HTTPConnect>(EndPointTypeFactory.CreateNetConnect("httphtml", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<HTTPConnect>(EndPointTypeFactory.CreateNetConnect("httpfull", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<DNSConnect>(EndPointTypeFactory.CreateNetConnect("dns", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<SMTPConnect>(EndPointTypeFactory.CreateNetConnect("smtp", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<QuantumConnect>(EndPointTypeFactory.CreateNetConnect("quantum", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<QuantumCertConnect>(EndPointTypeFactory.CreateNetConnect("quantumcert", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<SocketConnect>(EndPointTypeFactory.CreateNetConnect("rawconnect", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<NmapCmdConnect>(EndPointTypeFactory.CreateNetConnect("nmap", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<NmapCmdConnect>(EndPointTypeFactory.CreateNetConnect("nmapvuln", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<CrawlSiteCmdConnect>(EndPointTypeFactory.CreateNetConnect("crawlsite", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));
        Assert.IsType<CrawlSiteCmdConnect>(EndPointTypeFactory.CreateNetConnect("dailycrawl", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));

        var mockProvider = new Mock<ICmdProcessorProvider>();
        mockProvider.Setup(p => p.GetProcessor(It.IsAny<string>())).Returns((ICmdProcessor?)null);
        var mockBrowser = new Mock<IBrowserHost>();
        Assert.IsType<BleBroadcastConnect>(EndPointTypeFactory.CreateNetConnect("blebroadcast", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger, mockProvider.Object));
        Assert.IsType<BleBroadcastListenConnect>(EndPointTypeFactory.CreateNetConnect("blebroadcastlisten", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger, mockProvider.Object));
        Assert.IsType<HugSpaceKeepAliveConnect>(EndPointTypeFactory.CreateNetConnect("dailyhugkeepalive", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger, mockProvider.Object));
        Assert.IsType<SiteHashConnect>(EndPointTypeFactory.CreateNetConnect("sitehash", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger, browserHost: mockBrowser.Object));

        Assert.IsType<ICMPConnect>(EndPointTypeFactory.CreateNetConnect("notarealtype", httpClient, httpsClient, algoList, oqsProviderPath, commandPath, logger));

        mockProvider.Verify(p => p.GetProcessor("HugSpaceKeepAlive"), Times.Once());
    }
}
