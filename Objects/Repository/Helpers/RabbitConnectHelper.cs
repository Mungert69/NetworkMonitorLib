using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace NetworkMonitor.Objects.Repository
{
    public class RabbitConnectHelper
    {
        private const int MaxRetries = -1; // Maximum number of retries
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
        public static async Task<(bool success, IConnection? connection)> TryConnectAsync(
            string rabbitType,
            ConnectionFactory factory,
            ILogger logger,
            int maxRetries = MaxRetries,
            int retryDelayMilliseconds = RetryDelayMilliseconds,
            CancellationToken cancellationToken = default)
        {
            int retryCount = 0;
            while (maxRetries == -1 || retryCount < maxRetries)
            {
                try
                {
                    var connection = await factory.CreateConnectionAsync(cancellationToken);
                    if (connection.IsOpen) return (connection.IsOpen, connection); // Connection successful
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "{RabbitType} connection attempt to RabbitMQ server at {Host}:{Port} was cancelled.",
                        rabbitType, factory.HostName, factory.Port);
                    throw;
                }
                catch (Exception ex)
                {
                    var attempt = retryCount + 1;
                    object maxDisplay = maxRetries == -1 ? "infinite" : maxRetries;
                    var endpointDetails = SummarizeExceptionChain(ex);
                    var shouldRetry = IsRetryable(ex);

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

                    if (!shouldRetry)
                    {
                        logger.LogCritical(
                            "{RabbitType} connection failed with non-retryable error at {Host}:{Port}. Stopping retries on attempt {Attempt}.",
                            rabbitType, factory.HostName, factory.Port, attempt);
                        return (false, null);
                    }

                    retryCount++;
                    if (maxRetries != -1 && retryCount >= maxRetries)
                    {
                        break;
                    }
                }

                var delayMs = ComputeRetryDelayMs(retryCount, retryDelayMilliseconds);
                await Task.Delay(delayMs, cancellationToken);
            }
            return (false, null); // Connection failed after max retries
        }

        private static bool IsRetryable(Exception ex)
        {
            // Authentication/authorization errors are configuration issues and will not recover by waiting.
            if (ContainsType<AuthenticationFailureException>(ex)) return false;
            if (ContainsMessage(ex, "ACCESS_REFUSED")) return false;
            if (ContainsMessage(ex, "NOT_ALLOWED")) return false;
            if (ContainsMessage(ex, "authentication")) return false;
            if (ContainsMessage(ex, "vhost")) return false;
            return true;
        }

        private static bool ContainsType<TException>(Exception ex) where TException : Exception
        {
            if (ex is TException) return true;
            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    if (ContainsType<TException>(inner)) return true;
                }
            }

            return ex.InnerException != null && ContainsType<TException>(ex.InnerException);
        }

        private static bool ContainsMessage(Exception ex, string phrase)
        {
            if (!string.IsNullOrWhiteSpace(ex.Message) &&
                ex.Message.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    if (ContainsMessage(inner, phrase)) return true;
                }
            }

            return ex.InnerException != null && ContainsMessage(ex.InnerException, phrase);
        }

        private static int ComputeRetryDelayMs(int retryCount, int baseDelayMs)
        {
            // Exponential backoff capped at 10 minutes with +-20% jitter to avoid thundering herd.
            var safeBase = Math.Max(250, baseDelayMs);
            var exponent = Math.Min(retryCount, 6); // cap multiplier growth at 64x
            var scaled = safeBase * Math.Pow(2, exponent);
            var capped = Math.Min(600000, scaled);
            var jitterFactor = 0.8 + (Random.Shared.NextDouble() * 0.4);
            return (int)Math.Max(250, capped * jitterFactor);
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
