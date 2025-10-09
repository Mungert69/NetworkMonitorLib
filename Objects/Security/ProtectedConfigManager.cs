using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Connection;
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

        /*public async Task MigrateAsync( NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken = default)
        {
            if (_config == null) throw new ArgumentNullException(nameof(_config));
            if (netConfig == null) throw new ArgumentNullException(nameof(netConfig));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            foreach (var parameter in parameters)
            {
                try
                {
                    var configuredEntry = parameter.ConfigKeys
                        .Select(key => (Key: key, Value: _config[key]))
                        .FirstOrDefault(pair => !string.IsNullOrWhiteSpace(pair.Value));

                    var configuredValue = configuredEntry.Value ?? string.Empty;
                    var runtimeValue = parameter.Getter(netConfig) ?? string.Empty;
                    var envValue = Environment.GetEnvironmentVariable(parameter.EnvironmentVariableName) ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(configuredValue) && !string.Equals(configuredValue, parameter.Placeholder, StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrWhiteSpace(runtimeValue))
                        {
                            await _environmentStore.SetAsync(parameter.EnvironmentVariableName, runtimeValue, cancellationToken).ConfigureAwait(false);
                            await WritePlaceholdersAsync(netConfig, new[] { parameter }, cancellationToken).ConfigureAwait(false);
                            _logger.LogInformation("Migrated sensitive configuration value {ConfigKey} to environment variable {EnvVar}.", configuredEntry.Key, parameter.EnvironmentVariableName);
                        }
                    }
                    else if (string.Equals(configuredValue, parameter.Placeholder, StringComparison.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(envValue) && !string.IsNullOrWhiteSpace(runtimeValue))
                        {
                            await _environmentStore.SetAsync(parameter.EnvironmentVariableName, runtimeValue, cancellationToken).ConfigureAwait(false);
                            _logger.LogInformation("Seeded environment variable {EnvVar} from placeholder for {ConfigKey}.", parameter.EnvironmentVariableName, configuredEntry.Key);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to migrate sensitive configuration value for {EnvVar}.", parameter.EnvironmentVariableName);
                }
            }
        }*/

        public async Task SynchronizeSensitiveValuesAsync(
            NetConnectConfig netConfig,
            IEnumerable<ProtectedParameter> parameters,
            CancellationToken cancellationToken = default)
        {
            if (netConfig == null) throw new ArgumentNullException(nameof(netConfig));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            var parameterList = parameters.Distinct().ToList();
            if (parameterList.Count == 0)
            {
                return;
            }

            var placeholdersToWrite = new List<ProtectedParameter>();

            foreach (var parameter in parameterList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var configuredEntry = parameter.ConfigKeys
                    .Select(key => new { Key = key, Value = _config[key] })
                    .FirstOrDefault(entry => !string.IsNullOrWhiteSpace(entry.Value));

                var configuredValue = configuredEntry?.Value?.Trim();
                var runtimeValue = parameter.Getter(netConfig) ?? string.Empty;
                var envValue = Environment.GetEnvironmentVariable(parameter.EnvironmentVariableName) ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(configuredValue) &&
                    !string.Equals(configuredValue, parameter.Placeholder, StringComparison.Ordinal))
                {
                    var valueToPersist = string.IsNullOrEmpty(runtimeValue) ? configuredValue : runtimeValue;
                    parameter.Setter(netConfig, valueToPersist);

                    await _environmentStore.SetAsync(parameter.EnvironmentVariableName, valueToPersist, cancellationToken).ConfigureAwait(false);
                    placeholdersToWrite.Add(parameter);

                    if (configuredEntry != null)
                    {
                        try
                        {
                            _config[configuredEntry.Key] = parameter.Placeholder;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Unable to update configuration placeholder for {ConfigKey}.", configuredEntry.Key);
                        }
                    }

                    _logger.LogInformation(
                        "Migrated configuration key {ConfigKey} to environment variable {EnvVar}.",
                        configuredEntry?.Key ?? parameter.EnvironmentVariableName,
                        parameter.EnvironmentVariableName);

                    continue;
                }

                if (string.Equals(configuredValue, parameter.Placeholder, StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(envValue))
                    {
                        if (!string.Equals(runtimeValue, envValue, StringComparison.Ordinal))
                        {
                            parameter.Setter(netConfig, envValue);
                            _logger.LogInformation(
                                "Updated runtime value for {EnvVar} from environment variable.",
                                parameter.EnvironmentVariableName);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(runtimeValue))
                    {
                        await _environmentStore.SetAsync(parameter.EnvironmentVariableName, runtimeValue, cancellationToken).ConfigureAwait(false);
                        _logger.LogWarning(
                            "Environment variable {EnvVar} was missing; populated from runtime value.",
                            parameter.EnvironmentVariableName);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Environment variable {EnvVar} is missing and no value is available to seed.",
                            parameter.EnvironmentVariableName);
                    }
                }
                else if (string.IsNullOrWhiteSpace(configuredValue) &&
                         !string.IsNullOrWhiteSpace(runtimeValue) &&
                         string.IsNullOrWhiteSpace(envValue))
                {
                    await _environmentStore.SetAsync(parameter.EnvironmentVariableName, runtimeValue, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Seeded environment variable {EnvVar} from runtime configuration.",
                        parameter.EnvironmentVariableName);
                }
            }

            if (placeholdersToWrite.Count > 0)
            {
                await WritePlaceholdersAsync(netConfig, placeholdersToWrite, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task PersistAsync(ProtectedParameter parameter, NetConnectConfig netConfig, string value, CancellationToken cancellationToken = default)
        {
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            if (netConfig == null) throw new ArgumentNullException(nameof(netConfig));

            parameter.Setter(netConfig, value);
            await _environmentStore.SetAsync(parameter.EnvironmentVariableName, value, cancellationToken).ConfigureAwait(false);
            await WritePlaceholdersAsync(netConfig, new[] { parameter }, cancellationToken).ConfigureAwait(false);
        }

        public Task SaveConfigurationAsync(NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken = default)
        {
            if (netConfig == null) throw new ArgumentNullException(nameof(netConfig));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));

            return WritePlaceholdersAsync(netConfig, parameters, cancellationToken);
        }

        private async Task WritePlaceholdersAsync(NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken)
        {
            _fileRepo.CheckFileExists("appsettings.json", _logger);
            var list = parameters.Distinct().ToList();

            var originalValues = list.ToDictionary(p => p, p => p.Getter(netConfig));

            try
            {
                foreach (var parameter in list)
                {
                    parameter.Setter(netConfig, parameter.Placeholder);
                }

                await _fileRepo.SaveStateJsonAsync("appsettings.json", netConfig).ConfigureAwait(false);
            }
            finally
            {
                foreach (var parameter in list)
                {
                    var original = originalValues[parameter];
                    parameter.Setter(netConfig, original ?? string.Empty);
                }
            }
        }
    }
}
