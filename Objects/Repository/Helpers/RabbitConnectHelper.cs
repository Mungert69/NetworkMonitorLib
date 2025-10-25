using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.Repository
{
    public class RabbitConnectHelper
    {
        private const int MaxRetries = 100; // Maximum number of retries
        private const int RetryDelayMilliseconds = 5000; // Time to wait between retries

        public static string GetRoutingKey(string llmType, Dictionary<string, string> llmRunnerRoutingKeys)
        {
            // fallback to "" if not found (fanout case)
            return llmRunnerRoutingKeys.TryGetValue(llmType, out var key) ? key : "";
        }

        public static IEnumerable<string> GetAllUniqueRoutingKeys(Dictionary<string, string> llmRunnerRoutingKeys)
        {
            // Handles comma-separated lists for future expansion, and trims blanks
            return llmRunnerRoutingKeys.Values
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(k => k.Trim())
                .Distinct();
        }
        public static async Task<(bool success, IConnection? connection)> TryConnectAsync(string rabbitType, ConnectionFactory factory, ILogger logger, int maxRetries = MaxRetries, int retryDelayMilliseconds = RetryDelayMilliseconds)
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
                    var attempt = retryCount + 1;
                    object maxDisplay = maxRetries == -1 ? "infinite" : maxRetries;
                    var endpointDetails = SummarizeExceptionChain(ex);

                    if (!string.IsNullOrEmpty(endpointDetails))
                    {
                        logger.LogError(ex,
                            "{RabbitType} failed to connect to RabbitMQ server at {Host}:{Port}. Attempt {Attempt} of {Max}. Endpoint errors: {EndpointErrors}",
                            rabbitType, factory.HostName, factory.Port, attempt, maxDisplay, endpointDetails);
                    }
                    else
                    {
                        logger.LogError(ex,
                            "{RabbitType} failed to connect to RabbitMQ server at {Host}:{Port}. Attempt {Attempt} of {Max}.",
                            rabbitType, factory.HostName, factory.Port, attempt, maxDisplay);
                    }

                    retryCount++;
                    // Use await with Task.Delay for async delay
                }
                await Task.Delay(retryDelayMilliseconds);
            }
            return (false, null); // Connection failed after max retries
        }

        private static string SummarizeExceptionChain(Exception ex)
        {
            var summaries = new List<string>();
            void Walk(Exception? current)
            {
                if (current == null) return;
                summaries.Add($"{current.GetType().Name}: {current.Message}");
                if (current is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                        Walk(inner);
                }
                else
                {
                    Walk(current.InnerException);
                }
            }

            Walk(ex);
            return string.Join(" | ", summaries.Distinct());
        }
    }
}
