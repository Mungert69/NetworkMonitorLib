using System.Collections.Frozen;
using Microsoft.Extensions.Logging.Abstractions;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Factory;

namespace NetworkMonitor.Connection;

/// <summary>Immutable display metadata; never contains a sample or processor identity.</summary>
public sealed record EndpointMeasurementMetadata(string Unit = "ms", double Scale = 1, string Type = "");

/// <summary>Builds metadata from Connect properties without initializing or executing probes.</summary>
public static class EndpointMeasurementDefinitionBuilder
{
    public static FrozenDictionary<string, IReadOnlyList<EndpointMeasurementMetadata>> Build(
        IEnumerable<string> endpoints, Func<string, INetConnect> createConnect)
    {
        var definitions = new Dictionary<string, IReadOnlyList<EndpointMeasurementMetadata>>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in endpoints)
        {
            var connect = createConnect(endpoint);
            try
            {
                var metadata = Describe(endpoint, connect).Select(d =>
                    new EndpointMeasurementMetadata(d.Unit, d.Scale, d.Type)).ToArray();
                if (metadata.Length != 1 || metadata[0] != new EndpointMeasurementMetadata())
                    definitions.Add(endpoint, Array.AsReadOnly(metadata));
            }
            finally { connect.Cts.Dispose(); }
        }
        return definitions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<EndpointMeasurementDefinition> Describe(string endpoint, INetConnect connect)
    {
        var metadata = new[] { new EndpointMeasurementMetadata(connect.Unit, connect.Scale, connect.Type) }
            .Concat(connect.MeasurementVariants).ToArray();
        var definitions = metadata.Select(m => new EndpointMeasurementDefinition
            { EndpointType = endpoint.ToLowerInvariant(), Unit = m.Unit, Scale = m.Scale, Type = m.Type }).ToArray();
        if (definitions.Length > 64 || definitions.Any(d => !d.IsValid())
            || definitions.Select(d => d.Type).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Length)
            throw new InvalidOperationException("Invalid or duplicate Connect measurement definitions.");
        return definitions;
    }
}

/// <summary>Process-wide built-in catalogue. No database, broker, or platform configuration required.</summary>
public static class EndpointMeasurementDefaults
{
    private static readonly EndpointMeasurementMetadata Default = new();
    private static readonly Lazy<FrozenDictionary<string, IReadOnlyList<EndpointMeasurementMetadata>>> Definitions = new(() =>
    {
        using var http = new HttpClient();
        return EndpointMeasurementDefinitionBuilder.Build(EndPointTypeFactory.GetInternalTypes(), endpoint =>
            EndPointTypeFactory.CreateNetConnect(endpoint, http, http, new List<AlgorithmInfo>(),
                "", "", NullLogger.Instance));
    });

    public static EndpointMeasurementMetadata Get(string? endpoint, string? args = null) =>
        endpoint != null && Definitions.Value.TryGetValue(endpoint, out var metadata)
            ? EndpointMeasurementSelector.Resolve(metadata, args) : Default;
}
