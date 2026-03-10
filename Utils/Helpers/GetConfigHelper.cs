using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNetEnv;

namespace NetworkMonitor.Utils.Helpers
{
    public class GetConfigHelper
    {
        private static IConfiguration? _config;
        private static ILogger? _logger;
        private static readonly object _envLoadLock = new();
        private static readonly HashSet<string> _loadedEnvFiles = new(StringComparer.Ordinal);
        private static readonly HashSet<string> _missingEnvPaths = new(StringComparer.Ordinal);

        /// <summary>
        /// Call once at startup so you can use parameterless GetSection/GetConfigValue overloads.
        /// </summary>
        public static void Initialize(IConfiguration config, ILogger? logger = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger; // may be null; logging stays optional
        }

        // ---------- Convenience overloads (use Initialize) ----------

        /// <summary>
        /// Get a configuration value (with ".env" support) using the initialized config/logger.
        /// </summary>
        public static string GetConfigValue(string key, string defaultValue = "")
        {
            EnsureInitialized();
            return GetConfigValue(_logger, _config!, key, defaultValue);
        }

        /// <summary>
        /// Get a configuration section (with ".env" support) using the initialized config/logger.
        /// </summary>
        public static IConfigurationSection GetSection(string key)
        {
            EnsureInitialized();
            // Reuse the full-featured implementation below
            return GetSectionInternal(_config!, _logger, key);
        }

        // ---------- Existing (explicit) APIs remain available ----------

        public static string GetValueOrLogError(string key, string defaultValue, ILogger logger, IConfiguration config)
        {
            var value = config[key];
            if (value == null)
            {
                logger?.LogError($"Missing configuration for '{key}'.");
                return defaultValue;
            }
            return value;
        }

        public static string GetEnv(string key, string defaultValue = "")
        {
            var envVar = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrEmpty(envVar) ? defaultValue : envVar!;
        }

        public static string GetConfigValue(IConfiguration config, string key, string defaultValue = "")
        {
            // Non-logging version
            return ResolveConfigOrEnv(config, /*logger*/ null, key, defaultValue);
        }

        public static string GetConfigValue(ILogger? logger, IConfiguration config, string key, string defaultValue = "")
        {
            return ResolveConfigOrEnv(config, logger, key, defaultValue);
        }

        // ---------- Internal helpers ----------

        private static string ResolveConfigOrEnv(IConfiguration config, ILogger? logger, string key, string defaultValue)
        {
            var value = config.GetValue<string>(key) ?? defaultValue;

            if (string.Equals(value, ".env", StringComparison.Ordinal))
            {
                EnsureEnvironmentLoaded(config, logger);
                var envVar = Environment.GetEnvironmentVariable(key);
                if (string.IsNullOrEmpty(envVar))
                {
                    logger?.LogError($"Environment variable '{key}' is not set. Using default value.");
                    return defaultValue;
                }
                else
                {
                    logger?.LogInformation($"Environment variable '{key}' found; overriding from ENV.");
                    return envVar!;
                }
            }

            // Not overridden by .env sentinel
            logger?.LogInformation($"Configuration key '{key}' read from appsettings (no .env override).");
            return value;
        }

        /// <summary>
        /// Full implementation for .GetSection with ".env" support (arrays, scalars).
        /// Compatible with .Get&lt;T&gt; binding for lists/dictionaries/objects.
        /// </summary>
        private static IConfigurationSection GetSectionInternal(IConfiguration config, ILogger? logger, string key)
        {
            var section = config.GetSection(key);

            // Case 1: Section is a scalar and equals ".env" -> read ENV and synthesize
            if (string.Equals(section.Value, ".env", StringComparison.Ordinal))
            {
                EnsureEnvironmentLoaded(config, logger);
                var envVal = Environment.GetEnvironmentVariable(key);
                if (string.IsNullOrWhiteSpace(envVal))
                {
                    logger?.LogError($"Environment variable '{key}' is not set; returning empty section.");
                    var empty = new ConfigurationBuilder().AddInMemoryCollection().Build();
                    return empty.GetSection(key);
                }

                logger?.LogInformation($"Environment variable '{key}' found; overriding section from ENV.");

                var parts = envVal
                    .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();

                var data = new List<KeyValuePair<string, string?>>();
                if (parts.Length <= 1)
                {
                    // single scalar
                    data.Add(new KeyValuePair<string, string?>(key, envVal.Trim()));
                }
                else
                {
                    for (int i = 0; i < parts.Length; i++)
                        data.Add(new KeyValuePair<string, string?>($"{key}:{i}", parts[i]));
                }

                var memConfig = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
                return memConfig.GetSection(key);
            }

            // Case 2: Section has children; map any child that is ".env"
            var children = section.GetChildren().ToList();
            if (children.Count > 0)
            {
                var flattened = new List<KeyValuePair<string, string?>>();
                bool changed = false;

                foreach (var child in children)
                {
                    if (string.Equals(child.Value, ".env", StringComparison.Ordinal))
                    {
                        EnsureEnvironmentLoaded(config, logger);
                        changed = true;
                        // Child key ENV lookup uses the child’s full key path as the env name?
                        // Keeping behavior: look up ENV by child.Key (short key), which matches earlier code.
                        var envVal = Environment.GetEnvironmentVariable(child.Key);
                        if (string.IsNullOrWhiteSpace(envVal))
                        {
                            logger?.LogError($"Environment variable '{child.Key}' is not set; leaving empty.");
                            continue;
                        }
                        logger?.LogInformation($"Environment variable '{child.Key}' found; overriding section child from ENV.");
                        flattened.Add(new KeyValuePair<string, string?>($"{key}:{child.Key}", envVal.Trim()));
                    }
                    else
                    {
                        // Copy over nested structures/scalars
                        CopySection(child, $"{key}:{child.Key}", flattened);
                    }
                }

                if (changed)
                {
                    var mem = new ConfigurationBuilder().AddInMemoryCollection(flattened).Build();
                    return mem.GetSection(key);
                }
            }

            logger?.LogInformation($"Configuration section '{key}' read from appsettings (no .env override).");
            return section;
        }

        private static void CopySection(IConfigurationSection src, string prefix, List<KeyValuePair<string, string?>> dest)
        {
            var kids = src.GetChildren().ToList();
            if (kids.Count == 0)
            {
                if (src.Value is not null)
                    dest.Add(new KeyValuePair<string, string?>(prefix, src.Value));
                return;
            }

            foreach (var c in kids)
            {
                var p = $"{prefix}:{c.Key}";
                if (c.GetChildren().Any())
                    CopySection(c, p, dest);
                else if (c.Value is not null)
                    dest.Add(new KeyValuePair<string, string?>(p, c.Value));
            }
        }

        private static void EnsureInitialized()
        {
            if (_config is null)
                throw new InvalidOperationException("GetConfigHelper.Initialize(config, logger) must be called before using parameterless methods.");
        }

        private static void EnsureEnvironmentLoaded(IConfiguration config, ILogger? logger)
        {
            var configuredEnvPath = config["EnvPath"];
            var envPath = string.IsNullOrWhiteSpace(configuredEnvPath) ? ".env" : configuredEnvPath;
            var candidates = BuildEnvPathCandidates(envPath).ToList();
            var existingPath = candidates.FirstOrDefault(File.Exists);

            lock (_envLoadLock)
            {
                if (string.IsNullOrWhiteSpace(existingPath))
                {
                    if (_missingEnvPaths.Add(envPath))
                    {
                        logger?.LogWarning("No .env file found for EnvPath '{EnvPath}'. Checked: {Candidates}", envPath, string.Join(", ", candidates));
                    }
                    return;
                }

                var fullPath = Path.GetFullPath(existingPath);
                if (_loadedEnvFiles.Contains(fullPath))
                {
                    return;
                }

                try
                {
                    Env.Load(fullPath);
                    _loadedEnvFiles.Add(fullPath);
                    logger?.LogInformation("Loaded environment variables from: {EnvPath}", fullPath);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to load environment variables from: {EnvPath}", fullPath);
                }
            }
        }

        private static IEnumerable<string> BuildEnvPathCandidates(string envPath)
        {
            if (Path.IsPathRooted(envPath))
            {
                yield return Path.GetFullPath(envPath);
                yield break;
            }

            var currentDirectoryPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), envPath));
            yield return currentDirectoryPath;

            var appBasePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, envPath));
            if (!string.Equals(currentDirectoryPath, appBasePath, StringComparison.Ordinal))
            {
                yield return appBasePath;
            }
        }
    }
}
