using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects.Repository;
//using NetworkMonitor.Data.Services;
using Microsoft.Extensions.Logging;
using Betalgo.Ranul.OpenAI.ObjectModels.ResponseModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;
using System.Security.Cryptography;

namespace NetworkMonitor.Coordinator
{

    public interface IQueryCoordinator
    {
        Task<string> ExecuteQueryAsync(QueryIndexRequest queryIndexRequest,TimeSpan? timeout = null);
        void CompleteQuery(string messageId, string result, QueryIndexRequest? request = null);
        bool TryTakeCompletedRequest(string messageId, out QueryIndexRequest? request);
        void CancelQuery(string messageId);
        void RemoveSystemRag(List<ChatMessage> localHistory);
        Task AddSystemRag(string messageId, List<ChatMessage> localHistory);
    }
    public class QueryCoordinator : IQueryCoordinator
    {
        private readonly ConcurrentDictionary<int, (string result, DateTime timestamp)> _queryCache =
     new ConcurrentDictionary<int, (string, DateTime)>();
        private readonly TimeSpan _cacheTTL = TimeSpan.FromDays(5);
        private readonly SHA256 _hashAlgorithm = SHA256.Create(); // Changed from HashAlgorithm to SHA256
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingQueries =
            new ConcurrentDictionary<string, TaskCompletionSource<string>>();
        private readonly ConcurrentDictionary<string, string> _userQueries =
            new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, QueryIndexRequest> _completedRequests =
            new ConcurrentDictionary<string, QueryIndexRequest>();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _timeoutTokens =
            new ConcurrentDictionary<string, CancellationTokenSource>();


        private readonly IRabbitRepo _rabbitRepo;
        private readonly string _serviceID;
        private readonly string _authKey;
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
        private const string SystemRagMessage = "The following RAG data has been added.";

        private readonly ILogger _logger;
        private readonly string _routingKey;
        public QueryCoordinator(ILogger<QueryCoordinator> logger, IRabbitRepo rabbitRepo, SystemParams systemParams)
        {
            _logger = logger;
            _rabbitRepo = rabbitRepo;
            _serviceID = systemParams.ServiceID!;
            _authKey = systemParams.ServiceAuthKey ?? "Missing";
            _routingKey = systemParams.RabbitRoutingKey;

        }

        public async Task AddSystemRag(string messageId, List<ChatMessage> localHistory)
        {
            string ragResult = string.Empty;
            if (_userQueries.TryGetValue(messageId, out var userInput))
            {

                _logger.LogInformation($"Waiting for RAG result for message ID: {messageId}");

                if (_pendingQueries.TryGetValue(messageId, out var tcs))
                {
                    if (tcs.Task.IsCompleted)
                    {
                        _logger.LogInformation($"AddSystemRag: RAG result already handled for message ID {messageId}, skipping duplicate call.");
                        return;
                    }
                    // Wait for the RAG result (if the query is still pending)
                    try
                    {
                        ragResult = await tcs.Task;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error waiting for RAG result for message ID {messageId}: {ex.Message}");
                        return;
                    }
                }
                else
                {
                    _logger.LogWarning($"No pending or completed query found for message ID: {messageId}");
                    return;
                }

                // Add the RAG result as a system message
                if (!string.IsNullOrEmpty(ragResult))
                {
                    var systemMessage = ChatMessage.FromSystem($"{SystemRagMessage} Use it to answer the user's queries : " + ragResult);
                    localHistory.Add(systemMessage);
                    _logger.LogInformation($"RAG result added to chat history: {ragResult}");
                }
                else
                {
                    _logger.LogWarning($"RAG result for message ID {messageId} is empty.");
                }
            }
            else
            {
                _logger.LogWarning($"No user input found for message ID: {messageId}");
            }
        }
        public void RemoveSystemRag(List<ChatMessage> localHistory)
        {
            if (localHistory == null || localHistory.Count == 0)
            {
                _logger.LogWarning("Local history is null or empty. No system message to remove.");
                return;
            }

            // Find the first system message that contains RAG-related content
            var ragSystemMessage = localHistory.FirstOrDefault(m =>
                m.Role == "system" &&
                m.Content != null &&
                m.Content.StartsWith(SystemRagMessage));

            if (ragSystemMessage != null)
            {
                localHistory.Remove(ragSystemMessage);
                _logger.LogInformation("Removed RAG system message from local history.");
            }
            else
            {
                _logger.LogWarning("No RAG system message found in local history.");
            }
        }
        public async Task<string> ExecuteQueryAsync(QueryIndexRequest queryIndexRequest, TimeSpan? timeout = null)
        {
            var queryText=queryIndexRequest.QueryText;
            var messageId=queryIndexRequest.MessageID;
            var hashKey = GetQueryHash(queryText);
            if (_queryCache.TryGetValue(hashKey, out var cached) &&
                DateTime.UtcNow - cached.timestamp < _cacheTTL)
            {
                _logger.LogInformation($"QueryCoordinator cache hit for query='{queryText}'. Returning cached result.");
                return cached.result;
            }
            var tcs = new TaskCompletionSource<string>();
            if (!_pendingQueries.TryAdd(messageId, tcs) || !_userQueries.TryAdd(messageId, queryText))
            {
                _logger.LogWarning($"QueryCoordinator messageId={messageId} already pending; ignoring duplicate query.");
                return string.Empty;
            }


            var cts = new CancellationTokenSource();
            _timeoutTokens[messageId] = cts;

            _ = Task.Delay(timeout ?? _defaultTimeout, cts.Token)
                .ContinueWith(delayTask =>
                {
                    if (_pendingQueries.TryRemove(messageId, out var removedTcs) && !removedTcs.Task.IsCompleted)
                    {
                        _logger.LogWarning("QueryCoordinator timeout reached for messageId={MessageId}.", messageId);
                        removedTcs.TrySetException(new TimeoutException("Query timed out."));
                    }

                    _userQueries.TryRemove(messageId, out _);
                    if (_completedRequests.TryRemove(messageId, out _)) { }
                    if (_timeoutTokens.TryRemove(messageId, out var tokenSource))
                    {
                        tokenSource.Dispose();
                    }
                }, TaskContinuationOptions.OnlyOnRanToCompletion);

            queryIndexRequest.RoutingKey = _routingKey;

            // Publish the query to RabbitMQ
            _logger.LogInformation("QueryCoordinator publishing query messageId={MessageId}, index={Index}, routingKey='{RoutingKey}'", messageId, queryIndexRequest.IndexName, _routingKey);
            await _rabbitRepo.PublishAsync("queryIndex", queryIndexRequest);

            try
            {
                var result = await tcs.Task;
                // Only cache successful results
                _queryCache[hashKey] = (result, DateTime.UtcNow);
                _logger.LogInformation("QueryCoordinator received result for messageId={MessageId}.", messageId);
                return result;
            }
            catch
            {
                // Don't cache failed results
                throw;
            }
        }
        private int GetQueryHash(string queryText)
        {
            // Normalize the query
            var normalizedQuery = queryText?
                .Trim()
                .ToLowerInvariant()
                .Replace("\r", "")
                .Replace("\n", " ") ?? string.Empty;

            // Compute hash and convert to int
            byte[] hashBytes = _hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(normalizedQuery));
            return BitConverter.ToInt32(hashBytes, 0);
        }
        public void ClearCache()
        {
            _queryCache.Clear();
            _logger.LogInformation("RAG query cache cleared");
        }

        public void CompleteQuery(string messageId, string result, QueryIndexRequest? request = null)
        {
            if (_pendingQueries.TryGetValue(messageId, out var tcs))
            {
                tcs.TrySetResult(result);
                _pendingQueries.TryRemove(messageId, out _);
                _userQueries.TryRemove(messageId, out _);
                if (request != null)
                {
                    _completedRequests[messageId] = request;
                }
                if (_timeoutTokens.TryRemove(messageId, out var tokenSource))
                {
                    tokenSource.Cancel();
                    tokenSource.Dispose();
                }
                _logger.LogInformation($"RAG result completed for message ID: {messageId}");
            }
            else
            {
                _logger.LogWarning($"No pending query found to complete for message ID: {messageId}");
            }
        }

        public bool TryTakeCompletedRequest(string messageId, out QueryIndexRequest? request)
        {
            if (_completedRequests.TryRemove(messageId, out var stored))
            {
                request = stored;
                return true;
            }

            request = null;
            return false;
        }

        public void CancelQuery(string messageId)
        {
            if (_pendingQueries.TryGetValue(messageId, out var tcs))
            {
                tcs.TrySetCanceled();
                _pendingQueries.TryRemove(messageId, out _);
                _userQueries.TryRemove(messageId, out _);
                if (_timeoutTokens.TryRemove(messageId, out var tokenSource))
                {
                    tokenSource.Cancel();
                    tokenSource.Dispose();
                }
                _logger.LogInformation($"RAG query canceled for message ID: {messageId}");
            }
            else
            {
                _logger.LogWarning($"No pending query found to cancel for message ID: {messageId}");
            }
        }
    }
}
