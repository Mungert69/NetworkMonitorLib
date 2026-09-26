using System;
using System.Linq;

namespace NetworkMonitor.Objects.Repository.Helpers;

/// <summary>Routing identity for OAuth-authorized MQTT registration. The broker
/// must restrict write scopes to processor.register.&lt;token subject&gt;.*.
/// The original exchange MUST be checked: exchange-to-exchange bindings retain
/// it and otherwise allow a sender's legacy exchange to bypass topic ACLs.</summary>
public static class ProcessorMqttRegistration
{
    public const string Binding = "processor.register.*.*";
    public static bool TryGetOwner(string exchange, string route, out string owner)
    {
        owner = string.Empty;
        if (exchange != ProcessorMqttTopology.Exchange || route == null) return false;
        var parts = route.Split('.');
        if (parts.Length != 4 || parts[0] != "processor" || parts[1] != "register" ||
            !Guid.TryParseExact(parts[2], "D", out var id) || id.ToString("D") != parts[2] ||
            !ProcessorRabbitTopology.IsValidRoutingId(parts[3])) return false;
        owner = parts[2];
        return true;
    }

    public static ProcessorObj? Validate(string exchange, string route, ProcessorObj? input)
    {
        if (!TryGetOwner(exchange, route, out var owner) || input == null ||
            string.IsNullOrWhiteSpace(input.AppID) || input.AppID.Length > 255 ||
            !input.AppID.StartsWith(owner + "-", StringComparison.Ordinal) ||
            input.AppID.Length <= owner.Length + 1 ||
            (!string.IsNullOrEmpty(input.Owner) && input.Owner != owner) ||
            route != "processor.register." + owner + "." + ProcessorRabbitTopology.GetRoutingId(input.AppID) ||
            input.RabbitTopologyVersion != 2 || input.Location == null || input.Location.Length > 255 ||
            input.MaxLoad < 1 || input.MaxLoad > 100000 ||
            input.DisabledEndPointTypes == null || input.DisabledCommands == null ||
            input.DisabledEndPointTypes.Count > 128 || input.DisabledCommands.Count > 128 ||
            input.DisabledEndPointTypes.Concat(input.DisabledCommands).Any(s => s == null || s.Length > 128))
            return null;
        // Only registration fields, never credentials, broker destinations or backend signatures.
        return new ProcessorObj {
            AppID = input.AppID, Owner = owner, IsPrivate = true,
            Location = input.Location, MaxLoad = input.MaxLoad,
            IsQuantumCapable = input.IsQuantumCapable, RabbitTopologyVersion = 2,
            DisabledEndPointTypes = input.DisabledEndPointTypes.ToList(),
            DisabledCommands = input.DisabledCommands.ToList()
        };
    }
}
