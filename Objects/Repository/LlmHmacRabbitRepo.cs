using System;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Objects.Repository;

/// <summary>Signs RabbitMQ messages belonging to the less-trusted LLM trust domain.</summary>
public sealed class LlmHmacRabbitRepo : IRabbitRepo
{
    private readonly IRabbitRepo _inner;
    private readonly ILlmMessageHmacService _hmac;

    public LlmHmacRabbitRepo(IRabbitRepo inner, ILlmMessageHmacService hmac) { _inner = inner; _hmac = hmac; }
    public SystemUrl SystemUrl { get => _inner.SystemUrl; set => _inner.SystemUrl = value; }

    public async Task PublishAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class
    {
        await SignIfRequiredAsync(exchangeName, obj).ConfigureAwait(false);
        await _inner.PublishAsync(exchangeName, obj, routingKey).ConfigureAwait(false);
    }

    public async Task PublishAsync(string exchangeName, object? obj, string routingKey = "")
    {
        await SignIfRequiredAsync(exchangeName, obj).ConfigureAwait(false);
        await _inner.PublishAsync(exchangeName, obj, routingKey).ConfigureAwait(false);
    }

    private async Task SignIfRequiredAsync(string exchangeName, object? value)
    {
        if (value is QueryIndexRequest query &&
            TryResolve(exchangeName, out var resolvedOperation, out _) &&
            resolvedOperation == "queryIndexResult")
        {
            query.BackendSignature = await _hmac.SignAsync("queryIndexResult", query.AppID, query).ConfigureAwait(false);
            return;
        }
        if (!TryResolve(exchangeName, out var operation, out var target)) return;
        if (value is not IBackendSignedMessage message)
            throw new InvalidOperationException($"Protected LLM RabbitMQ operation '{exchangeName}' requires an IBackendSignedMessage payload.");
        if (value is QueryIndexRequest queryRequest) target = queryRequest.AppID;
        message.BackendSignature = await _hmac.SignAsync(operation, target, message).ConfigureAwait(false);
    }

    public static bool TryResolve(string exchange, out string operation, out string target)
        => MessageSecurityPolicyRegistry.TryResolve(
            exchange, string.Empty, MessageProtection.LlmHmac, out operation, out target);

    public string GetExchangeType(string exchangeName) => _inner.GetExchangeType(exchangeName);
    public Task Shutdown() => _inner.Shutdown();
    public Task<ResultObj> ConnectAndSetUp() => _inner.ConnectAndSetUp();
    public Task<ResultObj> ConnectAndSetUp(CancellationToken cancellationToken) => _inner.ConnectAndSetUp(cancellationToken);
    public Task<ResultObj> ConnectAndSetUp(CancellationToken cancellationToken, int? maxRetriesOverride) => _inner.ConnectAndSetUp(cancellationToken, maxRetriesOverride);
    public Task<ResultObj> ShutdownRepo() => _inner.ShutdownRepo();
    public Task<string> PublishJsonZAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class => _inner.PublishJsonZAsync(exchangeName, obj, routingKey);
    public Task<string> PublishJsonZWithIDAsync<T>(string exchangeName, T obj, string id, string routingKey = "") where T : class => _inner.PublishJsonZWithIDAsync(exchangeName, obj, id, routingKey);
}
