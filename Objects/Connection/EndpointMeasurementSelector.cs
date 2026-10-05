using System.Text.RegularExpressions;

namespace NetworkMonitor.Connection;

/// <summary>One distinct whole-token subtype selects metadata; ambiguity uses the general definition.</summary>
public static class EndpointMeasurementSelector
{
    private static readonly Regex Tokens = new(@"[\p{L}\p{N}_-]+", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static EndpointMeasurementMetadata Resolve(IEnumerable<EndpointMeasurementMetadata> definitions, string? args)
    {
        var items = definitions.ToArray();
        var general = items.FirstOrDefault(d => string.IsNullOrEmpty(d.Type)) ?? new EndpointMeasurementMetadata();
        if (string.IsNullOrEmpty(args)) return general;
        // A custom BLE encoding may change both scale and origin. Static display
        // metadata cannot describe it, so display the stored sample as raw.
        if (general.Unit == "raw value" && Regex.IsMatch(args, @"(?:^|\s)--metric_(?:scale|offset)(?:=|\s|$)", RegexOptions.IgnoreCase)) return general;
        string? format = BleMetricCatalogue.FormatFromArgs(args), metric = BleMetricCatalogue.MetricFromArgs(args);
        if (format != null && metric != null) {
            var encoding = BleMetricCatalogue.Find(format, metric);
            var definition = encoding == null ? null : items.FirstOrDefault(d => d.Type == encoding.Type)
                ?? items.FirstOrDefault(d => d.Type == BleMetricCatalogue.Find(format, System.Text.RegularExpressions.Regex.Replace(metric, @"_\d+$", ""))?.Type);
            if (definition != null) return definition;
        }
        HashSet<string> tokens;
        try { tokens = Tokens.Matches(args).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase); }
        catch (RegexMatchTimeoutException) { return general; }
        var matches = items.Where(d => !string.IsNullOrEmpty(d.Type) && tokens.Contains(d.Type)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : general;
    }
}
