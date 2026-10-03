using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils;

namespace NetworkMonitor.Connection;

/// <summary>A frozen, ordinal vocabulary for one dynamic-connect instance.</summary>
internal sealed class DynamicConnectStatusPolicy
{
    public const string InvalidStatus = "Invalid connect status";
    public const int MaximumLabels = 64;
    public const int MaximumLabelLength = 128;
    private readonly HashSet<string> _labels = new(StringComparer.Ordinal);
    private readonly ILogger _logger;

    public DynamicConnectStatusPolicy(IEnumerable<string> labels, ILogger logger)
    {
        _logger = logger;
        foreach (var label in labels)
        {
            if (_labels.Count >= MaximumLabels || string.IsNullOrWhiteSpace(label) ||
                label.Length > MaximumLabelLength || label != label.Trim() ||
                label.Any(char.IsControl) || !_labels.Add(label))
                throw new InvalidOperationException("StatusLabels must contain 1..64 unique, nonempty literal labels, each at most 128 characters, without surrounding whitespace or control characters.");
        }
        if (_labels.Count == 0)
            throw new InvalidOperationException("Declare StatusLabels using public override IReadOnlyCollection<string> StatusLabels => new[] { \"Service available\", \"Service unavailable\" }; Include every success and failure label; put changing values in monitor diagnostics.");
    }

    public void Validate(MPIConnect result)
    {
        string? status = result.PingInfo.Status;
        if (status == InvalidStatus || (status != null && _labels.Contains(status)))
            return;
        result.PingInfo.Status = InvalidStatus;
        // Preserve the diagnostic and outcome; only the lookup-table label changes.
        string rejected = StringUtils.Truncate(status ?? "<null>", MaximumLabelLength);
        result.Message = StringUtils.Truncate(
            $"Rejected undeclared connect status: {rejected}\n{result.Message}", StatusObj.MessageMaxLength);
        _logger.LogWarning("Dynamic connect produced an undeclared status; replaced with {Status}", InvalidStatus);
    }
}
