using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Connection;

namespace NetworkMonitor.Security
{
    public interface IProtectedConfigManager
    {
        //Task MigrateAsync( NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken = default);
        Task PersistAsync(ProtectedParameter parameter, NetConnectConfig netConfig, string value, CancellationToken cancellationToken = default);
        Task SynchronizeSensitiveValuesAsync(NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken = default);
        Task SaveConfigurationAsync(NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parameters, CancellationToken cancellationToken = default);
    }
}
