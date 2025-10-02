using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Connection;

namespace NetworkMonitor.Objects.Security
{
    public interface IProtectedConfigManager
    {
        Task MigrateAsync(IConfiguration config, NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken = default);
        Task PersistAsync(ProtectedParameter parameter, NetConnectConfig netConfig, string value, CancellationToken cancellationToken = default);
        Task SaveConfigurationAsync(NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken = default);
    }
}
