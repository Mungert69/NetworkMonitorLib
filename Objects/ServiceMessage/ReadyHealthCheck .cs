// Health/ReadyHealthCheck.cs
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NetworkMonitor.Objects;
using System.Threading;
using System.Threading.Tasks;
 

namespace NetworkMonitor.Objects.ServiceMessage;
public class ReadyHealthCheck : IHealthCheck
{
    private readonly IReadinessState _ready;
    public ReadyHealthCheck(IReadinessState ready) => _ready = ready;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext _, CancellationToken __)
        => Task.FromResult(_ready.IsReady
            ? HealthCheckResult.Healthy("Service's Rabbit listener is consuming.")
            : HealthCheckResult.Unhealthy("Service's Rabbit listener not ready."));
}
