using System.Collections.Generic;
using NetworkMonitor.Connection;

namespace NetworkMonitor.Objects.Security
{
    public static class ProtectedConfigurationParameters
    {
        public static readonly ProtectedParameter AuthKey = new(
            configKeys: new[] { "AuthKey" },
            environmentVariableName: "AuthKey",
            getter: net => net.AuthKey,
            setter: (net, value) => net.AuthKey = value ?? string.Empty);

        public static readonly ProtectedParameter RabbitPassword = new(
            configKeys: new[] { "LocalSystemUrl:RabbitPassword", "RabbitPassword" },
            environmentVariableName: "RabbitPassword",
            getter: net => net.LocalSystemUrl?.RabbitPassword ?? net.RabbitPassword,
            setter: (net, value) => net.RabbitPassword = value ?? string.Empty);

        public static readonly IReadOnlyList<ProtectedParameter> All = new[]
        {
            AuthKey,
            RabbitPassword
        };
    }
}
