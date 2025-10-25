using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNetEnv;
using Microsoft.Extensions.Logging;

namespace NetworkMonitor.Security
{
    public class EnvFileStore : IEnvironmentStore
    {
        private readonly ILogger? _logger;
        private readonly SemaphoreSlim _mutex = new(1, 1);

        public EnvFileStore(string envFilePath, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(envFilePath))
            {
                throw new ArgumentException("Environment file path must be provided", nameof(envFilePath));
            }

            EnvFilePath = Path.GetFullPath(envFilePath);
            _logger = logger;

            var directory = Path.GetDirectoryName(EnvFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public string EnvFilePath { get; }

        public void LoadIntoProcess()
        {
            try
            {
                if (File.Exists(EnvFilePath))
                {
                    var lines = File.ReadAllLines(EnvFilePath);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var trimmed = line.TrimStart();
                        if (trimmed.StartsWith('#') || trimmed.StartsWith(';')) continue;

                        var separatorIndex = line.IndexOf('=');
                        if (separatorIndex <= 0) continue;

                        var key = line.Substring(0, separatorIndex).Trim();
                        var value = line.Substring(separatorIndex + 1).Trim();

                        // Remove surrounding quotes if present
                        if (value.StartsWith("\"") && value.EndsWith("\"") && value.Length > 1)
                        {
                            value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
                        }

                        Environment.SetEnvironmentVariable(key, value);
                        _logger?.LogDebug("Set environment variable {Key} from file {Path}", key, EnvFilePath);
                    }
                    _logger?.LogInformation("Loaded environment variables from {Path}", EnvFilePath);
                }
                else
                {
                    _logger?.LogInformation("No environment file found at {Path}; continuing without loading", EnvFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load environment variables from {Path}", EnvFilePath);
            }
        }

        public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key must be provided", nameof(key));
            }

            await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var normalizedValue = value ?? string.Empty;
                if (string.Equals(normalizedValue.Trim(), ".env", StringComparison.Ordinal))
                {
                    _logger?.LogWarning("Skipped writing placeholder value for {Key} to {Path}", key, EnvFilePath);
                    return;
                }

                var lines = new List<string>();
                bool replaced = false;

                if (File.Exists(EnvFilePath))
                {
                    var existing = await File.ReadAllLinesAsync(EnvFilePath, cancellationToken).ConfigureAwait(false);
                    foreach (var line in existing)
                    {
                        if (TryMatchKey(line, key))
                        {
                            lines.Add(FormatLine(key, normalizedValue));
                            replaced = true;
                        }
                        else
                        {
                            lines.Add(line);
                        }
                    }
                }

                if (!replaced)
                {
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    {
                        lines.Add(string.Empty);
                    }
                    lines.Add(FormatLine(key, normalizedValue));
                }

                await File.WriteAllLinesAsync(EnvFilePath, lines, cancellationToken).ConfigureAwait(false);
                Environment.SetEnvironmentVariable(key, normalizedValue);
                _logger?.LogInformation("Updated environment variable {Key} in file {Path}", key, EnvFilePath);
            }
            finally
            {
                _mutex.Release();
            }
        }

        private static bool TryMatchKey(string line, string key)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#') || trimmed.StartsWith(';')) return false;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0) return false;

            var currentKey = line.Substring(0, separatorIndex).Trim();
            return string.Equals(currentKey, key, StringComparison.Ordinal);
        }

        private static string FormatLine(string key, string value)
        {
            return string.IsNullOrWhiteSpace(value) ? $"{key}=" : $"{key}={Quote(value)}";
        }

        private static string Quote(string value)
        {
            if (value.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '#', '\"' }) == -1)
            {
                return value;
            }

            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
    }
}
