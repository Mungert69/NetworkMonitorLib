using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using System.Text.Json;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection;

public class ConfigIntegrityConnectTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly string _resultPath;

    public ConfigIntegrityConnectTests()
    {
        Directory.CreateDirectory(_directory);
        _resultPath = Path.Combine(_directory, "result.json");
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Connect_CleanResult_ReportsUp()
    {
        await WriteResultAsync(0, "clean", findings: []);
        var connect = CreateConnect();

        await connect.Connect();

        Assert.True(connect.MpiConnect.IsUp);
        Assert.Equal("Configuration integrity clean", connect.MpiConnect.PingInfo.Status);
        Assert.Contains("Checked: 5", connect.MpiConnect.Message);
    }

    [Fact]
    public async Task Connect_DifferenceResult_ReportsDownWithFinding()
    {
        await WriteResultAsync(1, "integrity_differences", findings:
        [new { state = "CHANGED", path = "/etc/nginx/nginx.conf", package = "nginx-common" }]);
        var connect = CreateConnect();

        await connect.Connect();

        Assert.False(connect.MpiConnect.IsUp);
        Assert.Equal("Configuration integrity differences", connect.MpiConnect.PingInfo.Status);
        Assert.Contains("CHANGED /etc/nginx/nginx.conf [nginx-common]", connect.MpiConnect.Message);
    }

    [Fact]
    public async Task Connect_OperationalError_ReportsDown()
    {
        await WriteResultAsync(2, "operational_error", findings: [], error: "baseline is not valid JSON");
        var connect = CreateConnect();

        await connect.Connect();

        Assert.False(connect.MpiConnect.IsUp);
        Assert.Equal("Configuration integrity operational error", connect.MpiConnect.PingInfo.Status);
        Assert.Contains("baseline is not valid JSON", connect.MpiConnect.Message);
    }

    [Fact]
    public async Task Connect_InvalidSchema_ReportsDown()
    {
        await File.WriteAllTextAsync(_resultPath, "{\"schema\":\"unknown\",\"exit_code\":0}");
        var connect = CreateConnect();

        await connect.Connect();

        Assert.False(connect.MpiConnect.IsUp);
        Assert.Equal("Configuration integrity result invalid", connect.MpiConnect.PingInfo.Status);
    }

    [Fact]
    public async Task Connect_MissingResult_ReportsDown()
    {
        var connect = CreateConnect();

        await connect.Connect();

        Assert.False(connect.MpiConnect.IsUp);
        Assert.Equal("Configuration integrity result unavailable", connect.MpiConnect.PingInfo.Status);
    }

    private ConfigIntegrityConnect CreateConnect() => new(_resultPath)
    {
        MpiStatic = new MPIStatic { Address = "localhost", Timeout = 5000, EndPointType = "configintegrity" },
    };

    private Task WriteResultAsync(int exitCode, string status, object[] findings, string? error = null)
    {
        var payload = new
        {
            schema = "config-integrity-result/v1",
            exit_code = exitCode,
            status,
            summary = new Dictionary<string, int>
            {
                ["checked"] = 5,
                ["changed"] = exitCode == 1 ? 1 : 0,
                ["new"] = 0,
                ["removed"] = 0,
                ["restored"] = 0,
            },
            findings,
            error,
        };
        return File.WriteAllTextAsync(_resultPath, JsonSerializer.Serialize(payload));
    }
}
