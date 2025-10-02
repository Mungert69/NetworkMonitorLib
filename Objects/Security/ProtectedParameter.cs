using System;
using System.Collections.Generic;
using NetworkMonitor.Connection;

namespace NetworkMonitor.Objects.Security
{
    public sealed class ProtectedParameter
    {
        public ProtectedParameter(
            IEnumerable<string> configKeys,
            string environmentVariableName,
            Func<NetConnectConfig, string?> getter,
            Action<NetConnectConfig, string> setter,
            string placeholder = ".env")
        {
            ConfigKeys = configKeys != null ? new List<string>(configKeys).AsReadOnly() : throw new ArgumentNullException(nameof(configKeys));
            if (ConfigKeys.Count == 0) throw new ArgumentException("At least one configuration key must be provided.", nameof(configKeys));
            EnvironmentVariableName = environmentVariableName ?? throw new ArgumentNullException(nameof(environmentVariableName));
            Getter = getter ?? throw new ArgumentNullException(nameof(getter));
            Setter = setter ?? throw new ArgumentNullException(nameof(setter));
            Placeholder = placeholder ?? ".env";
        }

        public IReadOnlyList<string> ConfigKeys { get; }
        public string EnvironmentVariableName { get; }
        public Func<NetConnectConfig, string?> Getter { get; }
        public Action<NetConnectConfig, string> Setter { get; }
        public string Placeholder { get; }
    }
}
