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
    public static FrozenDictionary<string, EndpointMeasurementMetadata> Build(
        IEnumerable<string> endpoints, Func<string, INetConnect> createConnect)
    {
        var definitions = new Dictionary<string, EndpointMeasurementMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in endpoints)
        {
            var connect = createConnect(endpoint);
            try
            {
                var definition = new EndpointMeasurementDefinition
                    { EndpointType = endpoint, Unit = connect.Unit, Scale = connect.Scale, Type = connect.Type };
                if (!definition.IsValid())
                    throw new InvalidOperationException($"Invalid built-in measurement definition: {endpoint}");
                var metadata = new EndpointMeasurementMetadata(definition.Unit, definition.Scale, definition.Type);
                if (metadata != new EndpointMeasurementMetadata()) definitions.Add(endpoint, metadata);
            }
            finally { connect.Cts.Dispose(); }
        }
        return definitions.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Process-wide built-in catalogue. No database, broker, or platform configuration required.</summary>
public static class EndpointMeasurementDefaults
{
    private static readonly EndpointMeasurementMetadata Default = new();
    private static readonly Lazy<FrozenDictionary<string, EndpointMeasurementMetadata>> Definitions = new(() =>
    {
        using var http = new HttpClient();
        return EndpointMeasurementDefinitionBuilder.Build(EndPointTypeFactory.GetInternalTypes(), endpoint =>
            EndPointTypeFactory.CreateNetConnect(endpoint, http, http, new List<AlgorithmInfo>(),
                "", "", NullLogger.Instance));
    });

    public static EndpointMeasurementMetadata Get(string? endpoint) =>
        endpoint != null && Definitions.Value.TryGetValue(endpoint, out var metadata) ? metadata : Default;
}
