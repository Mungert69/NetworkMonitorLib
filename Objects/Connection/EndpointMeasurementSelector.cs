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
        HashSet<string> tokens;
        try { tokens = Tokens.Matches(args).Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase); }
        catch (RegexMatchTimeoutException) { return general; }
        var matches = items.Where(d => !string.IsNullOrEmpty(d.Type) && tokens.Contains(d.Type)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : general;
    }
}
