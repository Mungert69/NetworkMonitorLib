using NetworkMonitor.Connection;
using NetworkMonitor.DTOs;

namespace NetworkMonitor.Objects.Factory;

public sealed record EndpointDocumentationQuery(string Endpoint, string? Format = null, string? Record = null, string? Metric = null);

public interface IEndpointDocumentationProvider
{
    string Endpoint { get; }
    EndpointDocumentationResponse Describe(EndpointDocumentationQuery query);
}

/// <summary>Routes discovery requests; providers own endpoint-specific configuration documentation.</summary>
public sealed class EndpointDocumentationCatalogue(IEnumerable<IEndpointDocumentationProvider> providers)
{
    private readonly IReadOnlyDictionary<string, IEndpointDocumentationProvider> _providers = providers.ToDictionary(p => p.Endpoint, StringComparer.OrdinalIgnoreCase);

    public EndpointDocumentationResponse Describe(EndpointDocumentationQuery request)
    {
        static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        var query = new EndpointDocumentationQuery(request.Endpoint.Trim().ToLowerInvariant(), Normalize(request.Format), Normalize(request.Record), Normalize(request.Metric));
        if (query.Format == null && (query.Record != null || query.Metric != null))
            throw new ArgumentException("Specify format before record or metric.");
        if (_providers.TryGetValue(query.Endpoint, out var provider)) return provider.Describe(query);
        if (query.Format != null) throw new ArgumentException("BLE format filters require a BLE endpoint.");
        return new EndpointDocumentationResponse {
            Endpoint = query.Endpoint,
            Description = EndPointTypeFactory.GetEndpointTypes().FirstOrDefault(e => e.InternalType == query.Endpoint)?.Description
                ?? "Registered custom Connect; configuration documentation is not available."
        };
    }
}

/// <summary>Default composition for built-in discovery; callers can compose a catalogue with additional providers.</summary>
public static class EndpointDocumentation
{
    private static readonly EndpointDocumentationCatalogue Catalogue = new(new IEndpointDocumentationProvider[] {
        new BleBroadcastDocumentationProvider(new IBleFormatDocumentationProvider[] {
            new VictronDocumentationProvider(),
            new MetricBleDocumentationProvider("ruuvi", "Ruuvi RAWv2 is unencrypted; leave password empty.", "temperature",
                "Ruuvi RAWv2 metrics; unavailable field markers do not produce valid readings."),
            new MetricBleDocumentationProvider("bthome", "Leave password empty for unencrypted packets; encrypted BTHome requires its 16-byte key.", "temperature",
                "Available objects depend on the device; repeated objects use suffixes _2, _3, etc."),
            new GenericBleDocumentationProvider("raw"), new GenericBleDocumentationProvider("aesgcm"), new GenericBleDocumentationProvider("aesctr")
        }),
        new BleListenDocumentationProvider()
    });

    public static EndpointDocumentationResponse Describe(string endpoint, string? format = null, string? record = null, string? metric = null) =>
        Catalogue.Describe(new(endpoint, format, record, metric));
}
