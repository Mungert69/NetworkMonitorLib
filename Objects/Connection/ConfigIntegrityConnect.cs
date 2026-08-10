using System.Text;
using System.Text.Json;

namespace NetworkMonitor.Connection;

/// <summary>
/// Reads the sanitized result published by the root-owned config-integrity service.
/// This connect never executes config-integrity or any other host command.
/// </summary>
public sealed class ConfigIntegrityConnect : NetConnect
{
    public const string DefaultResultPath = "/run/config-integrity/result.json";
    private const string ResultSchema = "config-integrity-result/v1";
    private readonly string _resultPath;

    public ConfigIntegrityConnect(string resultPath = DefaultResultPath)
    {
        _resultPath = resultPath;
    }

    public override async Task Connect()
    {
        PreConnect();
        Timer.Restart();
        try
        {
            var json = await File.ReadAllTextAsync(_resultPath, Cts.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != ResultSchema)
            {
                ProcessException("Unsupported or missing result schema", "Configuration integrity result invalid");
                return;
            }
            if (!root.TryGetProperty("exit_code", out var exitCodeElement) ||
                !exitCodeElement.TryGetInt32(out var exitCode))
            {
                ProcessException("Missing result exit code", "Configuration integrity result invalid");
                return;
            }

            var summary = FormatSummary(root);
            var findings = FormatFindings(root);
            var guidance = FormatConsumerGuidance(root);
            var detail = CombineDetail(summary, findings, guidance);
            var elapsed = (ushort)Math.Min(Timer.ElapsedMilliseconds, ushort.MaxValue);

            switch (exitCode)
            {
                case 0:
                    ProcessStatus("Configuration integrity clean", elapsed, detail);
                    break;
                case 1:
                    ProcessException(detail, "Configuration integrity differences");
                    break;
                case 2:
                    var error = root.TryGetProperty("error", out var errorElement)
                        ? errorElement.GetString()
                        : null;
                    ProcessException(error ?? detail, "Configuration integrity operational error");
                    break;
                default:
                    ProcessException($"Unexpected result exit code: {exitCode}", "Configuration integrity result invalid");
                    break;
            }
        }
        catch (OperationCanceledException) when (Cts.IsCancellationRequested)
        {
            ProcessException("Timed out reading configuration integrity result", "Configuration integrity timeout");
        }
        catch (Exception exception)
        {
            ProcessException(exception.Message, "Configuration integrity result unavailable");
        }
        finally
        {
            Timer.Stop();
            PostConnect();
        }
    }

    private static string FormatSummary(JsonElement root)
    {
        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
        {
            return "Configuration integrity result has no summary.";
        }

        static int GetCount(JsonElement summary, string name) =>
            summary.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) ? count : 0;

        return $"Checked: {GetCount(summary, "checked")} | Changed: {GetCount(summary, "changed")} | " +
               $"New: {GetCount(summary, "new")} | Removed: {GetCount(summary, "removed")} | " +
               $"Restored: {GetCount(summary, "restored")}";
    }

    private static string FormatFindings(JsonElement root)
    {
        if (!root.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var lines = new StringBuilder();
        foreach (var finding in findings.EnumerateArray().Take(10))
        {
            var state = finding.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : "UNKNOWN";
            var path = finding.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : "(path unavailable)";
            var package = finding.TryGetProperty("package", out var packageElement) ? packageElement.GetString() : null;
            if (lines.Length > 0) lines.AppendLine();
            lines.Append(state).Append(' ').Append(path);
            if (!string.IsNullOrWhiteSpace(package)) lines.Append(" [").Append(package).Append(']');
        }
        return lines.ToString();
    }

    private static string FormatConsumerGuidance(JsonElement root)
    {
        if (!root.TryGetProperty("consumer_guidance", out var guidance) ||
            guidance.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var lines = new StringBuilder();
        foreach (var item in guidance.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var instruction = item.GetString();
            if (string.IsNullOrWhiteSpace(instruction))
            {
                continue;
            }

            if (lines.Length > 0)
            {
                lines.AppendLine();
            }
            lines.Append("- ").Append(instruction.Trim());
        }

        return lines.Length == 0 ? string.Empty : $"LLM guidance:\n{lines}";
    }

    private static string CombineDetail(string summary, string findings, string guidance)
    {
        var sections = new[] { summary, findings, guidance }
            .Where(section => !string.IsNullOrWhiteSpace(section));
        return string.Join("\n\n", sections);
    }
}
