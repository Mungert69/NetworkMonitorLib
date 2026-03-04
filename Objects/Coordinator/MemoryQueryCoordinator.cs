using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;

namespace NetworkMonitor.Coordinator;

public interface IMemoryQueryCoordinator
{
    Task<string> ExecuteMemoryQueryAsync(MemoryQueryRequest memoryQueryRequest, TimeSpan? timeout = null);
    Task<string> ExecuteMemoryTurnWindowAsync(MemoryTurnWindowRequest request, TimeSpan? timeout = null);
    void CompleteMemoryQuery(string messageId, string result, MemoryQueryRequest? request = null);
    void CompleteMemoryTurnWindow(string messageId, string result, MemoryTurnWindowRequest? request = null);
    bool TryTakeCompletedMemoryRequest(string messageId, out MemoryQueryRequest? request);
    bool TryTakeCompletedMemoryTurnWindow(string messageId, out MemoryTurnWindowRequest? request);
    void CancelMemoryQuery(string messageId);
    void CancelMemoryTurnWindow(string messageId);
}

public class MemoryQueryCoordinator : IMemoryQueryCoordinator
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingQueries =
        new ConcurrentDictionary<string, TaskCompletionSource<string>>();
    private readonly ConcurrentDictionary<string, MemoryQueryRequest> _completedRequests =
        new ConcurrentDictionary<string, MemoryQueryRequest>();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timeoutTokens =
        new ConcurrentDictionary<string, CancellationTokenSource>();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingTurnWindows =
        new ConcurrentDictionary<string, TaskCompletionSource<string>>();
    private readonly ConcurrentDictionary<string, MemoryTurnWindowRequest> _completedTurnWindows =
        new ConcurrentDictionary<string, MemoryTurnWindowRequest>();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timeoutTurnWindows =
        new ConcurrentDictionary<string, CancellationTokenSource>();

    private readonly IRabbitRepo _rabbitRepo;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
    private readonly ILogger _logger;
    private readonly string _routingKey;

    public MemoryQueryCoordinator(ILogger<MemoryQueryCoordinator> logger, IRabbitRepo rabbitRepo, SystemParams systemParams)
    {
        _logger = logger;
        _rabbitRepo = rabbitRepo;
        _routingKey = systemParams.RabbitRoutingKey;
    }

    public async Task<string> ExecuteMemoryQueryAsync(MemoryQueryRequest memoryQueryRequest, TimeSpan? timeout = null)
    {
        var messageId = memoryQueryRequest.MessageID;
        var tcs = new TaskCompletionSource<string>();

        if (!_pendingQueries.TryAdd(messageId, tcs))
        {
            _logger.LogWarning("MemoryQueryCoordinator messageId={MessageId} already pending; ignoring duplicate.", messageId);
            return string.Empty;
        }

        var cts = new CancellationTokenSource();
        _timeoutTokens[messageId] = cts;

        _ = Task.Delay(timeout ?? _defaultTimeout, cts.Token)
            .ContinueWith(delayTask =>
            {
                if (_pendingQueries.TryRemove(messageId, out var removedTcs) && !removedTcs.Task.IsCompleted)
                {
                    _logger.LogWarning("MemoryQueryCoordinator timeout reached for messageId={MessageId}.", messageId);
                    removedTcs.TrySetException(new TimeoutException("Memory query timed out."));
                }

                _completedRequests.TryRemove(messageId, out _);
                if (_timeoutTokens.TryRemove(messageId, out var tokenSource))
                {
                    tokenSource.Dispose();
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

        memoryQueryRequest.RoutingKey = _routingKey;

        _logger.LogInformation("MemoryQueryCoordinator publishing memory query messageId={MessageId}, routingKey='{RoutingKey}'", messageId, _routingKey);
        await _rabbitRepo.PublishAsync("queryMemory", memoryQueryRequest);

        return await tcs.Task;
    }

    public async Task<string> ExecuteMemoryTurnWindowAsync(MemoryTurnWindowRequest request, TimeSpan? timeout = null)
    {
        var messageId = request.MessageID;
        var tcs = new TaskCompletionSource<string>();

        if (!_pendingTurnWindows.TryAdd(messageId, tcs))
        {
            _logger.LogWarning("MemoryQueryCoordinator turn-window messageId={MessageId} already pending; ignoring duplicate.", messageId);
            return string.Empty;
        }

        var cts = new CancellationTokenSource();
        _timeoutTurnWindows[messageId] = cts;

        _ = Task.Delay(timeout ?? _defaultTimeout, cts.Token)
            .ContinueWith(delayTask =>
            {
                if (_pendingTurnWindows.TryRemove(messageId, out var removedTcs) && !removedTcs.Task.IsCompleted)
                {
                    _logger.LogWarning("MemoryQueryCoordinator turn-window timeout reached for messageId={MessageId}.", messageId);
                    removedTcs.TrySetException(new TimeoutException("Memory turn window timed out."));
                }

                _completedTurnWindows.TryRemove(messageId, out _);
                if (_timeoutTurnWindows.TryRemove(messageId, out var tokenSource))
                {
                    tokenSource.Dispose();
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

        request.RoutingKey = _routingKey;

        _logger.LogInformation("MemoryQueryCoordinator publishing turn-window messageId={MessageId}, routingKey='{RoutingKey}'", messageId, _routingKey);
        await _rabbitRepo.PublishAsync("queryMemoryTurnWindow", request);

        return await tcs.Task;
    }

    public void CompleteMemoryQuery(string messageId, string result, MemoryQueryRequest? request = null)
    {
        if (_pendingQueries.TryGetValue(messageId, out var tcs))
        {
            tcs.TrySetResult(result);
            _pendingQueries.TryRemove(messageId, out _);

            if (request != null)
            {
                _completedRequests[messageId] = request;
            }

            if (_timeoutTokens.TryRemove(messageId, out var tokenSource))
            {
                tokenSource.Cancel();
                tokenSource.Dispose();
            }

            _logger.LogInformation("Memory query completed for message ID: {MessageId}", messageId);
        }
        else
        {
            _logger.LogWarning("No pending memory query found to complete for message ID: {MessageId}", messageId);
        }
    }

    public bool TryTakeCompletedMemoryRequest(string messageId, out MemoryQueryRequest? request)
    {
        if (_completedRequests.TryRemove(messageId, out var stored))
        {
            request = stored;
            return true;
        }

        request = null;
        return false;
    }

    public bool TryTakeCompletedMemoryTurnWindow(string messageId, out MemoryTurnWindowRequest? request)
    {
        if (_completedTurnWindows.TryRemove(messageId, out var stored))
        {
            request = stored;
            return true;
        }

        request = null;
        return false;
    }

    public void CancelMemoryQuery(string messageId)
    {
        if (_pendingQueries.TryGetValue(messageId, out var tcs))
        {
            tcs.TrySetCanceled();
            _pendingQueries.TryRemove(messageId, out _);
            _completedRequests.TryRemove(messageId, out _);

            if (_timeoutTokens.TryRemove(messageId, out var tokenSource))
            {
                tokenSource.Cancel();
                tokenSource.Dispose();
            }

            _logger.LogInformation("Canceled pending memory query for message ID: {MessageId}", messageId);
        }
    }

    public void CompleteMemoryTurnWindow(string messageId, string result, MemoryTurnWindowRequest? request = null)
    {
        if (_pendingTurnWindows.TryGetValue(messageId, out var tcs))
        {
            tcs.TrySetResult(result);
            _pendingTurnWindows.TryRemove(messageId, out _);

            if (request != null)
            {
                _completedTurnWindows[messageId] = request;
            }

            if (_timeoutTurnWindows.TryRemove(messageId, out var tokenSource))
            {
                tokenSource.Cancel();
                tokenSource.Dispose();
            }

            _logger.LogInformation("Memory turn window completed for message ID: {MessageId}", messageId);
        }
        else
        {
            _logger.LogWarning("No pending memory turn window found to complete for message ID: {MessageId}", messageId);
        }
    }

    public void CancelMemoryTurnWindow(string messageId)
    {
        if (_pendingTurnWindows.TryGetValue(messageId, out var tcs))
        {
            tcs.TrySetCanceled();
            _pendingTurnWindows.TryRemove(messageId, out _);
            _completedTurnWindows.TryRemove(messageId, out _);

            if (_timeoutTurnWindows.TryRemove(messageId, out var tokenSource))
            {
                tokenSource.Cancel();
                tokenSource.Dispose();
            }

            _logger.LogInformation("Canceled pending memory turn window for message ID: {MessageId}", messageId);
        }
    }
}
