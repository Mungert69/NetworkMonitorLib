using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;

namespace NetworkMonitor.Security
{
    public class ProtectedConfigManager : IProtectedConfigManager
    {
        private readonly IEnvironmentStore _environmentStore;
        private readonly IFileRepo _fileRepo;
        private readonly ILogger _logger;
        private readonly IConfiguration _config;

        public ProtectedConfigManager(IConfiguration config, IEnvironmentStore environmentStore, IFileRepo fileRepo, ILogger<ProtectedConfigManager> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _environmentStore = environmentStore ?? throw new ArgumentNullException(nameof(environmentStore));
            _fileRepo = fileRepo ?? throw new ArgumentNullException(nameof(fileRepo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task PersistAndSaveAsync(
            NetConnectConfig netConfig,
            IEnumerable<ProtectedParameter> parametersToSave,
            CancellationToken cancellationToken = default)
        {
            if (netConfig == null) throw new ArgumentNullException(nameof(netConfig));
            if (parametersToSave == null) throw new ArgumentNullException(nameof(parametersToSave));

            var list = parametersToSave.Distinct().ToList();

            foreach (var parameter in list)
            {
                var value = parameter.Getter(netConfig) ?? string.Empty;
                parameter.Setter(netConfig, value);
                await _environmentStore
                    .SetAsync(parameter.EnvironmentVariableName, value, cancellationToken)
                    .ConfigureAwait(false);
            }

            await WritePlaceholdersAsync(netConfig, list, cancellationToken).ConfigureAwait(false);
        }

        private async Task WritePlaceholdersAsync(NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken)
        {
            _fileRepo.CheckFileExists("appsettings.json", _logger);
            var list = parameters.Distinct().ToList();
            var netConfigJson = JsonSerializer.Serialize(netConfig, netConfig.GetType(), SourceGenerationContext.Default);
            var snapshotNode = JsonNode.Parse(netConfigJson) as JsonObject ?? new JsonObject();

            foreach (var parameter in list)
            {
                foreach (var key in parameter.ConfigKeys)
                {
                    SetJsonPathValue(snapshotNode, key, parameter.Placeholder);
                }
            }

            var snapshotJson = snapshotNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await _fileRepo.SaveStateStringAsync("appsettings.json", snapshotJson).ConfigureAwait(false);
        }

        private static void SetJsonPathValue(JsonObject root, string path, string value)
        {
            if (root == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var segments = path.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return;
            }

            JsonObject current = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                var segment = segments[i];
                var child = current[segment] as JsonObject;
                if (child == null)
                {
                    child = new JsonObject();
                    current[segment] = child;
                }
                current = child;
            }

            current[segments[^1]] = value;
        }
    }
}
