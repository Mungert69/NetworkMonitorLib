using System;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.Repository.Helpers
{
    /// <summary>
    /// Publishes ProcessorAgent commands through the v2 shared topic exchange.
    /// Callers remain on the legacy exchange-per-operation path until a processor
    /// explicitly advertises v2 support.
    /// </summary>
    public static class ProcessorRabbitPublisher
    {
        public static Task PublishAsync<T>(
            IRabbitRepo rabbitRepo,
            string routingId,
            string operation,
            T message,
            int topologyVersion)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(rabbitRepo);
            ArgumentNullException.ThrowIfNull(message);

            if (ProcessorRabbitTopology.NormalizeVersion(topologyVersion) == ProcessorRabbitTopology.LegacyVersion)
            {
                return rabbitRepo.PublishAsync(operation + routingId, message);
            }

            return rabbitRepo.PublishAsync(
                ProcessorRabbitTopology.CommandsExchange,
                message,
                ProcessorRabbitTopology.BuildRoutingKey(routingId, operation));
        }

        public static Task PublishAsync(
            IRabbitRepo rabbitRepo,
            string routingId,
            string operation,
            object? message,
            int topologyVersion)
        {
            ArgumentNullException.ThrowIfNull(rabbitRepo);
            if (ProcessorRabbitTopology.NormalizeVersion(topologyVersion) == ProcessorRabbitTopology.LegacyVersion)
            {
                return rabbitRepo.PublishAsync(operation + routingId, message);
            }

            return rabbitRepo.PublishAsync(
                ProcessorRabbitTopology.CommandsExchange,
                message,
                ProcessorRabbitTopology.BuildRoutingKey(routingId, operation));
        }
    }
}
