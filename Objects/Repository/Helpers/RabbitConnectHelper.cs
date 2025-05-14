using RabbitMQ.Client;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.Repository
{
    public class RabbitConnectHelper
    {
        private const int MaxRetries = 100; // Maximum number of retries
        private const int RetryDelayMilliseconds = 5000; // Time to wait between retries

        public static async Task<(bool success, IConnection? connection)> TryConnectAsync(string rabbitType,ConnectionFactory factory, ILogger logger, int maxRetries = MaxRetries, int retryDelayMilliseconds = RetryDelayMilliseconds)
        {
            int retryCount = 0;
            while (maxRetries == -1 || retryCount < maxRetries)
            {
                try
                {
                    var connection = await factory.CreateConnectionAsync();
                    if (connection.IsOpen) return (connection.IsOpen, connection); // Connection successful
                }
                catch (Exception ex)
                {
                    logger.LogError($"{rabbitType } failed to connect to RabbitMQ server at {factory.HostName}:{factory.Port}. Attempt {retryCount + 1} of {maxRetries}. Error: {ex.Message}");
                    retryCount++;
                   // Use await with Task.Delay for async delay
                }
                  await Task.Delay(retryDelayMilliseconds);
            }
            return (false, null); // Connection failed after max retries
        }
    }
}
