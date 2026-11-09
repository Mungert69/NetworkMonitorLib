using NetworkMonitor.Connection;
using NetworkMonitor.DTOs;

namespace NetworkMonitor.Objects.Factory;

public interface IBleFormatDocumentationProvider
{
    string Format { get; }
    EndpointDocumentationResponse Describe(EndpointDocumentationQuery query);
}

internal static class BleArgumentDocumentation
{
    public static IReadOnlyDictionary<string, EndpointArgumentDocumentation> Select(bool listen, params string[] names) =>
        (listen ? BleCommandArguments.CreateListen() : BleCommandArguments.CreateBroadcast())
            .Where(a => names.Contains(a.Key)).ToDictionary(a => a.Key,
                a => new EndpointArgumentDocumentation(a.Required, a.TypeHint, a.DefaultValue, a.Help));

    public static EndpointConfigurationExample Example(string format, string? metric = null) =>
        new("blebroadcast", $"--format {format}" + (metric == null ? "" : $" --metric {metric}"), "AA:BB:CC:DD:EE:FF");
}

public sealed class BleBroadcastDocumentationProvider(IEnumerable<IBleFormatDocumentationProvider> providers) : IEndpointDocumentationProvider
{
    private readonly IReadOnlyDictionary<string, IBleFormatDocumentationProvider> _formats = providers.ToDictionary(p => p.Format, StringComparer.OrdinalIgnoreCase);
    public string Endpoint => "blebroadcast";

    public EndpointDocumentationResponse Describe(EndpointDocumentationQuery query)
    {
        if (query.Format != null)
        {
            if (!_formats.TryGetValue(query.Format, out var provider))
                throw new ArgumentException("Unknown format. Valid formats: " + string.Join(", ", _formats.Keys));
            return provider.Describe(query);
        }
        return new() {
            Endpoint = Endpoint,
            Description = "Monitor one selected BLE metric from a specific address. Put the device address in address and any encryption key in password. Select the format explicitly; use one metric per monitor.",
            Arguments = BleArgumentDocumentation.Select(false, "format", "metric", "payload", "manufacturer_id", "service_uuid"),
            Formats = _formats.Keys.ToArray(), Next = "Specify format for its key requirements, metrics or device records."
        };
    }
}

public sealed class BleListenDocumentationProvider : IEndpointDocumentationProvider
{
    public string Endpoint => "blebroadcastlisten";
    public EndpointDocumentationResponse Describe(EndpointDocumentationQuery query)
    {
        if (query.Format != null) throw new ArgumentException("Listen mode returns raw advertisements; use blebroadcast for format and metric selection.");
        return new() {
            Endpoint = Endpoint,
            Description = "Discover raw BLE advertisements during the timeout window; no address required. Does not decrypt or record a physical metric.",
            Arguments = BleArgumentDocumentation.Select(true, "payload", "manufacturer_id", "service_uuid", "max_captures"),
            Example = new(Endpoint, "--payload manufacturer", null),
            Next = "Choose an observed address and use blebroadcast to monitor a decoded metric."
        };
    }
}

public sealed class GenericBleDocumentationProvider(string format) : IBleFormatDocumentationProvider
{
    public string Format => format;
    public EndpointDocumentationResponse Describe(EndpointDocumentationQuery query)
    {
        if (query.Record != null || query.Metric != null) throw new ArgumentException("Generic payload formats do not supply named physical metrics.");
        return new() {
            Endpoint = query.Endpoint, Format = Format,
            Description = "Raw or generic decrypted payloads do not supply named physical metrics; do not interpret the numeric fallback as a sensor reading.",
            Key = Format == "raw" ? "No key needed." : "Put an AES key in password; packet layout must match the selected nonce/tag settings.",
            Arguments = BleArgumentDocumentation.Select(false, Format switch {
                "aesgcm" => new[] { "nonce_len", "nonce_at", "tag_len" }, "aesctr" => new[] { "nonce_len", "nonce_at" }, _ => Array.Empty<string>()
            }),
            Example = BleArgumentDocumentation.Example(Format)
        };
    }
}
