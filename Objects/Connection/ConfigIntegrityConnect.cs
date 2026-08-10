using System.Text;
using System.Text.Json;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Connection;

/// <summary>
/// Reads the sanitized result published by the root-owned config-integrity service.
/// This connect never executes config-integrity or any other host command.
/// </summary>
public sealed class ConfigIntegrityConnect : NetConnect
{
    public const string DefaultResultPath = "/run/config-integrity/result.json";
    private const string ResultSchema = "config-integrity-result/v1";
    private const int ErrorDetailLimit = 512;
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
                ProcessIntegrityFailure("Configuration integrity result invalid", "Unsupported or missing result schema");
                return;
            }
            if (!root.TryGetProperty("exit_code", out var exitCodeElement) ||
                !exitCodeElement.TryGetInt32(out var exitCode))
            {
                ProcessIntegrityFailure("Configuration integrity result invalid", "Missing result exit code");
                return;
            }

            var summary = FormatSummary(root);
            var resultFields = FormatResultFields(root, exitCode, summary);
            var findings = FormatFindings(root);
            var guidance = FormatConsumerGuidance(root);
            var elapsed = (ushort)Math.Min(Timer.ElapsedMilliseconds, ushort.MaxValue);

            switch (exitCode)
            {
                case 0:
                    var cleanStatus = "Configuration integrity clean";
                    var cleanDetail = BuildBoundedDetail(
                        resultFields,
                        findings,
                        guidance,
                        AvailableDetailLength(cleanStatus));
                    ProcessStatus(cleanStatus, elapsed, cleanDetail);
                    break;
                case 1:
                    const string differencesStatus = "Configuration integrity differences detected";
                    var differencesDetail = BuildBoundedDetail(
                        resultFields,
                        findings,
                        guidance,
                        AvailableDetailLength(differencesStatus));
                    ProcessIntegrityFailure(differencesStatus, differencesDetail);
                    break;
                case 2:
                    var error = root.TryGetProperty("error", out var errorElement)
                        ? errorElement.GetString()
                        : null;
                    const string operationalStatus = "Configuration integrity operational error";
                    var errorFields = string.IsNullOrWhiteSpace(error)
                        ? resultFields
                        : $"{resultFields}\nError: {LimitErrorDetail(error)}";
                    var operationalDetail = BuildBoundedDetail(
                        errorFields,
                        findings,
                        guidance,
                        AvailableDetailLength(operationalStatus));
                    ProcessIntegrityFailure(operationalStatus, operationalDetail);
                    break;
                default:
                    const string invalidStatus = "Configuration integrity result invalid";
                    ProcessIntegrityFailure(invalidStatus, $"Unexpected result exit code: {exitCode}");
                    break;
            }
        }
        catch (OperationCanceledException) when (Cts.IsCancellationRequested)
        {
            ProcessIntegrityFailure("Configuration integrity timeout", "Timed out reading configuration integrity result");
        }
        catch (Exception exception)
        {
            ProcessIntegrityFailure("Configuration integrity result unavailable", LimitErrorDetail(exception.Message));
        }
        finally
        {
            Timer.Stop();
            PostConnect();
        }
    }

    private void ProcessIntegrityFailure(string status, string detail)
    {
        MpiConnect.Message = $"CONFIGINTEGRITY: {status}\n{detail}";
        MpiConnect.IsUp = false;
        MpiConnect.PingInfo.Status = status;
        MpiConnect.PingInfo.RoundTripTime = UInt16.MaxValue;
    }

    private static int AvailableDetailLength(string status) =>
        StatusObj.MessageMaxLength - $"CONFIGINTEGRITY: {status}\n".Length;

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

    private static string FormatResultFields(JsonElement root, int exitCode, string summary)
    {
        var checkedAt = root.TryGetProperty("checked_at", out var checkedAtElement)
            ? checkedAtElement.GetString()
            : null;
        var status = root.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;

        return $"Checked at: {checkedAt ?? "unavailable"}\n" +
               $"Result: {status ?? "unknown"} (exit code {exitCode})\n" +
               summary;
    }

    private static List<string> FormatFindings(JsonElement root)
    {
        if (!root.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var finding in findings.EnumerateArray())
        {
            var state = finding.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : "UNKNOWN";
            var path = finding.TryGetProperty("path", out var pathElement) ? pathElement.GetString() : "(path unavailable)";
            var package = finding.TryGetProperty("package", out var packageElement) ? packageElement.GetString() : null;
            var line = new StringBuilder().Append(state).Append(' ').Append(path);
            if (!string.IsNullOrWhiteSpace(package)) line.Append(" [").Append(package).Append(']');
            lines.Add(line.ToString());
        }
        return lines;
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

    private static string BuildBoundedDetail(
        string resultFields,
        IReadOnlyList<string> findings,
        string guidance,
        int maximumLength)
    {
        var bestFindings = string.Empty;
        for (var shown = 0; shown <= findings.Count; shown++)
        {
            var findingSection = FormatFindingSection(findings, shown);
            var candidateSections = new List<string> { resultFields };
            if (!string.IsNullOrWhiteSpace(findingSection)) candidateSections.Add(findingSection);
            if (!string.IsNullOrWhiteSpace(guidance)) candidateSections.Add(guidance);
            var candidate = string.Join("\n\n", candidateSections);
            if (candidate.Length > maximumLength)
            {
                break;
            }
            bestFindings = findingSection;
        }

        var sections = new List<string> { resultFields };
        if (!string.IsNullOrWhiteSpace(bestFindings)) sections.Add(bestFindings);
        if (!string.IsNullOrWhiteSpace(guidance)) sections.Add(guidance);
        var detail = string.Join("\n\n", sections);

        return detail.Length <= maximumLength
            ? detail
            : LimitGuidanceAtBoundary(resultFields, guidance, maximumLength);
    }

    private static string FormatFindingSection(IReadOnlyList<string> findings, int shown)
    {
        if (findings.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string> { $"Findings shown: {shown} of {findings.Count}." };
        lines.AddRange(findings.Take(shown));
        if (shown < findings.Count)
        {
            lines.Add($"{findings.Count - shown} additional finding(s) omitted to keep this status within 4 KB.");
            lines.Add("Ask an administrator to run: sudo config-integrity check --verbose");
        }
        return string.Join("\n", lines);
    }

    private static string LimitGuidanceAtBoundary(string resultFields, string guidance, int maximumLength)
    {
        var detail = resultFields;
        if (string.IsNullOrWhiteSpace(guidance))
        {
            return resultFields[..Math.Min(resultFields.Length, maximumLength)];
        }

        var guidanceLines = guidance.Split('\n');
        foreach (var line in guidanceLines)
        {
            var separator = detail == resultFields ? "\n\n" : "\n";
            var candidate = detail + separator + line;
            if (candidate.Length > maximumLength)
            {
                const string notice = "Guidance was shortened to keep this status within 4 KB.";
                if (detail.Length + separator.Length + notice.Length <= maximumLength)
                {
                    detail += separator + notice;
                }
                break;
            }
            detail = candidate;
        }
        return detail;
    }

    private static string LimitErrorDetail(string? error)
    {
        if (string.IsNullOrWhiteSpace(error) || error.Length <= ErrorDetailLimit)
        {
            return error ?? string.Empty;
        }
        return $"{error[..ErrorDetailLimit]}… (truncated)";
    }
}
