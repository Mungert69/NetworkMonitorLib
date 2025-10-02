using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.Security
{
    public interface IEnvironmentStore
    {
        string EnvFilePath { get; }
        void LoadIntoProcess();
        Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    }
}
