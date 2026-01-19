using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;

public class ConnectFactoryTest
{
    private class DummyLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private NetConnectConfig GetConfig()
    {
        var dummyConfig = new ConfigurationBuilder().Build();
        var config = new NetConnectConfig(dummyConfig, "TestSection");

        // Create a temp directory and empty AlgoTable.csv and curves
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(tempDir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "AlgoTable.csv"), "");
        System.IO.File.WriteAllText(System.IO.Path.Combine(tempDir, "curves"), "");

        config.CommandPath = "/bin/echo"; // Use a dummy path that exists
        config.OqsProviderPath = tempDir; // Use the temp directory with AlgoTable.csv and curves
        return config;
    }

    [Fact]
    public void GetNetConnectObj_ReturnsCorrectType()
    {
        var logger = new DummyLogger();
        var config = GetConfig();
        var factory = new ConnectFactory(logger, config, null, null, null);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "http",
            Address = "http://example.com",
            Enabled = true,
            Port = 80,
            Timeout = 1000,
            Username = "user",
            Password = "pass",
            Args = "--metric pv_power"
        };
        var pingParams = new PingParams { Timeout = 1000 };

        var nc = factory.GetNetConnectObj(mpi, pingParams);
        Assert.NotNull(nc);
        Assert.Equal("http", nc.MpiStatic.EndPointType);
        Assert.Equal("http://example.com", nc.MpiStatic.Address);
        Assert.Equal(80, nc.MpiStatic.Port);
        Assert.Equal(1000, nc.MpiStatic.Timeout);
        Assert.Equal("user", nc.MpiStatic.Username);
        Assert.Equal("pass", nc.MpiStatic.Password);
        Assert.Equal("--metric pv_power", nc.MpiStatic.Args);
    }

    [Fact]
    public void UpdateNetConnectionInfo_UpdatesFields()
    {
        var logger = new DummyLogger();
        var config = GetConfig();
        var factory = new ConnectFactory(logger, config, null, null, null);

        var mpi = new MonitorPingInfo
        {
            MonitorIPID = 1,
            EndPointType = "http",
            Address = "http://example.com",
            Enabled = true,
            Port = 80,
            Timeout = 1000,
            Username = "user",
            Password = "pass",
            Args = "--metric pv_power"
        };
        var pingParams = new PingParams { Timeout = 1000 };
        var nc = factory.GetNetConnectObj(mpi, pingParams);

        // Update fields
        mpi.Address = "http://changed.com";
        mpi.Enabled = false;
        mpi.Port = 8080;
        mpi.Timeout = 2000;
        mpi.Username = "changeduser";
        mpi.Password = "changedpass";
        mpi.Args = "--metric battery_voltage";

        factory.UpdateNetConnectionInfo(nc, mpi, pingParams);

        Assert.Equal("http://changed.com", nc.MpiStatic.Address);
        Assert.False(nc.MpiStatic.Enabled);
        Assert.Equal(8080, nc.MpiStatic.Port);
        Assert.Equal(2000, nc.MpiStatic.Timeout);
        Assert.Equal("changeduser", nc.MpiStatic.Username);
        Assert.Equal("changedpass", nc.MpiStatic.Password);
        Assert.Equal("--metric battery_voltage", nc.MpiStatic.Args);
    }
}
