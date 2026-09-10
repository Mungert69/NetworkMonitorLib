using System;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects.Repository.Helpers;
using RabbitMQ.Client;

namespace NetworkMonitor.Objects.Repository;

public enum RabbitConnectionRole { Publisher, Consumer }

/// <summary>Owns shared, long-lived AMQP sockets for a consolidated host.</summary>
public interface IRabbitConnectionPool : IAsyncDisposable
{
    Task<IConnection> GetConnectionAsync(SystemUrl systemUrl, RabbitConnectionRole role, ILogger logger, CancellationToken cancellationToken);
}

public sealed class RabbitConnectionPool : IRabbitConnectionPool
{
    private readonly ConcurrentDictionary<RabbitConnectionKey, ConnectionEntry> _connections = new();
    private volatile bool _disposed;

    public async Task<IConnection> GetConnectionAsync(SystemUrl systemUrl, RabbitConnectionRole role, ILogger logger, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = RabbitConnectionKey.From(systemUrl, role);
        return await _connections.GetOrAdd(key, static _ => new ConnectionEntry())
            .GetOrCreateAsync(key, systemUrl, logger, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var entry in _connections.Values)
            await entry.DisposeAsync();
        _connections.Clear();
    }

    private sealed class ConnectionEntry : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IConnection? _connection;

        public async Task<IConnection> GetOrCreateAsync(RabbitConnectionKey key, SystemUrl systemUrl, ILogger logger, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                // Existing connections recover automatically. Do not make a
                // competing socket while recovery is in progress.
                if (_connection != null) return _connection;

                var factory = new ConnectionFactory
                {
                    HostName = systemUrl.RabbitHostName,
                    UserName = systemUrl.RabbitUserName,
                    Password = systemUrl.RabbitPassword,
                    VirtualHost = systemUrl.RabbitVHost,
                    AutomaticRecoveryEnabled = true,
                    TopologyRecoveryEnabled = true,
                    Port = systemUrl.RabbitPort,
                    RequestedHeartbeat = TimeSpan.FromSeconds(120),
                    HandshakeContinuationTimeout = TimeSpan.FromSeconds(40),
                    ClientProvidedName = $"NetworkMonitorLLM shared {key.Role.ToString().ToLowerInvariant()}",
                    Ssl = BuildSslOption(systemUrl, logger)
                };
                var (success, connection) = await RabbitConnectHelper.TryConnectAsync(
                    $"shared {key.Role.ToString().ToLowerInvariant()} RabbitMQ", factory, logger,
                    maxRetries: -1, cancellationToken: cancellationToken);
                if (!success || connection == null)
                    throw new InvalidOperationException("Unable to establish the shared RabbitMQ connection.");
                _connection = connection;
                return connection;
            }
            finally { _gate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_connection != null)
                {
                    if (_connection.IsOpen) await _connection.CloseAsync();
                    _connection.Dispose();
                    _connection = null;
                }
            }
            finally { _gate.Release(); _gate.Dispose(); }
        }
    }

    private static SslOption BuildSslOption(SystemUrl systemUrl, ILogger logger)
    {
        var sslOption = new SslOption { Enabled = systemUrl.UseTls, ServerName = systemUrl.RabbitHostName, AcceptablePolicyErrors = SslPolicyErrors.None };
        LegacyAndroidSslHelper.Configure(systemUrl, sslOption, logger);
        return sslOption;
    }

    // RabbitInstanceName is queue naming, not a broker identity boundary.
    private sealed record RabbitConnectionKey(string Host, ushort Port, string UserName, string Password, string VirtualHost, bool UseTls, int AndroidSdkLevel, string LegacyRootCertPath, string LegacyIntermediateUrl, RabbitConnectionRole Role)
    {
        public static RabbitConnectionKey From(SystemUrl url, RabbitConnectionRole role) => new(
            url.RabbitHostName, url.RabbitPort, url.RabbitUserName, url.RabbitPassword,
            url.RabbitVHost, url.UseTls, url.AndroidSdkLevel, url.LegacyAndroidRootCertPath,
            url.LegacyIntermediateUrl, role);
    }
}
