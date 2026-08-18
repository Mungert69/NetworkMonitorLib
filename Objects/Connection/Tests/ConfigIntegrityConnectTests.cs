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
        Assert.Contains("Result: clean (exit code 0)", connect.MpiConnect.Message);
    }

    [Fact]
    public async Task Connect_DifferenceResult_ReportsDownWithFinding()
    {
        await WriteResultAsync(1, "integrity_differences", findings:
        [new { state = "CHANGED", path = "/etc/nginx/nginx.conf", package = "nginx-common" }],
        guidance:
        [
            "Review the affected path before trusting it.",
            "Only an administrator may run: sudo config-integrity update.",
        ]);
        var connect = CreateConnect();

        await connect.Connect();

        Assert.False(connect.MpiConnect.IsUp);
        Assert.Equal("Configuration integrity differences detected", connect.MpiConnect.PingInfo.Status);
        Assert.StartsWith("CONFIGINTEGRITY: Configuration integrity differences detected", connect.MpiConnect.Message);
        Assert.DoesNotContain("Failed to connect", connect.MpiConnect.Message);
        Assert.Contains("CHANGED /etc/nginx/nginx.conf [nginx-common]", connect.MpiConnect.Message);
        Assert.Contains("Findings shown: 1 of 1.", connect.MpiConnect.Message);
        Assert.Contains("LLM guidance:", connect.MpiConnect.Message);
        Assert.Contains("Review the affected path before trusting it.", connect.MpiConnect.Message);
        Assert.Contains("Only an administrator may run: sudo config-integrity update.", connect.MpiConnect.Message);
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

    [Fact]
    public async Task Connect_ManyFindings_ShowsOnlyCompleteEntriesWithinStatusLimit()
    {
        var findings = Enumerable.Range(1, 80)
            .Select(index => new
            {
                state = "CHANGED",
                path = $"/etc/config-integrity-test-{index}-{new string('x', 80)}",
                package = "example-package",
            })
            .Cast<object>()
            .ToArray();
        await WriteResultAsync(
            1,
            "integrity_differences",
            findings,
            guidance:
            [
                "Review every affected path before trusting a new baseline.",
                "Only an administrator may run: sudo config-integrity update.",
            ]);
        var connect = CreateConnect();

        await connect.Connect();

        Assert.True(connect.MpiConnect.Message!.Length <= StatusObj.MessageMaxLength);
        Assert.Contains("Findings shown:", connect.MpiConnect.Message);
        Assert.Contains("additional finding(s) omitted", connect.MpiConnect.Message);
        Assert.Contains("sudo config-integrity check --verbose", connect.MpiConnect.Message);
        Assert.Contains("CHANGED /etc/config-integrity-test-1-", connect.MpiConnect.Message);
        Assert.DoesNotContain("CHANGED /etc/config-integrity-test-80-", connect.MpiConnect.Message);
        Assert.Contains("LLM guidance:", connect.MpiConnect.Message);
    }

    private ConfigIntegrityConnect CreateConnect() => new(_resultPath)
    {
        MpiStatic = new MPIStatic { Address = "localhost", Timeout = 5000, EndPointType = "configintegrity" },
    };

    private Task WriteResultAsync(
        int exitCode,
        string status,
        object[] findings,
        string? error = null,
        string[]? guidance = null)
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
            consumer_guidance = guidance ?? [],
        };
        return File.WriteAllTextAsync(_resultPath, JsonSerializer.Serialize(payload));
    }
}
