using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Connection;

namespace NetworkMonitor.Security
{
    public interface IProtectedConfigManager
    {
        Task PersistAndSaveAsync(NetConnectConfig netConfig, IEnumerable<ProtectedParameter> parametersToSave, CancellationToken cancellationToken = default);
    }
}
