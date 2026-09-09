using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Objects.Repository.Helpers;

namespace NetworkMonitor.Objects.Repository;

/// <summary>
/// The single signing decorator for trusted backend RabbitMQ publishers.
/// It signs processor commands and Data control messages, then delegates all
/// transport behavior to the ordinary RabbitRepo.
/// </summary>
public sealed class BackendSignedRabbitRepo : IRabbitRepo
{
    private readonly IRabbitRepo _inner;
    private readonly IBackendMessageSignatureService _signatureService;

    public BackendSignedRabbitRepo(IRabbitRepo inner, IBackendMessageSignatureService signatureService)
    {
        _inner = inner;
        _signatureService = signatureService;
    }

    public SystemUrl SystemUrl { get => _inner.SystemUrl; set => _inner.SystemUrl = value; }

    public async Task PublishAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class
    {
        if (string.Equals(exchangeName, "fullProcessorList", StringComparison.Ordinal) && obj is List<ProcessorObj> processors)
        {
            var snapshot = new ProcessorStateSnapshot { Processors = processors };
            await SignIfRequiredAsync(exchangeName, snapshot).ConfigureAwait(false);
            await _inner.PublishAsync(exchangeName, snapshot, routingKey).ConfigureAwait(false);
            return;
        }

        if (MessageSecurityPolicyRegistry.WrapsEmailBatch(exchangeName) && obj is List<GenericEmailObj> emails)
        {
            var batch = new GenericEmailBatch { Emails = emails };
            await SignIfRequiredAsync(exchangeName, batch).ConfigureAwait(false);
            await _inner.PublishAsync(exchangeName, batch, routingKey).ConfigureAwait(false);
            return;
        }

        await SignIfRequiredAsync(exchangeName, obj, routingKey).ConfigureAwait(false);
        await _inner.PublishAsync(exchangeName, obj, routingKey).ConfigureAwait(false);
    }

    public async Task PublishAsync(string exchangeName, object? obj, string routingKey = "")
    {
        if (obj == null && MessageSecurityPolicyRegistry.IsPayloadFreeMlDsa(exchangeName))
        {
            var command = new BackendControlCommand();
            await SignIfRequiredAsync(exchangeName, command).ConfigureAwait(false);
            await _inner.PublishAsync(exchangeName, command, routingKey).ConfigureAwait(false);
            return;
        }

        await SignIfRequiredAsync(exchangeName, obj, routingKey).ConfigureAwait(false);
        await _inner.PublishAsync(exchangeName, obj, routingKey).ConfigureAwait(false);
    }

    private async Task SignIfRequiredAsync(string exchangeName, object? obj, string routingKey = "")
    {
        if (!TryResolveTarget(exchangeName, routingKey, out var operation, out var target)) return;
        if (obj is not IBackendSignedMessage message)
            throw new InvalidOperationException($"Protected RabbitMQ operation '{exchangeName}' requires an IBackendSignedMessage payload.");
        message.BackendSignature = await _signatureService.SignAsync(operation, target, message).ConfigureAwait(false);
    }

    public static bool TryResolveTarget(string exchangeName, out string operation, out string target)
        => TryResolveTarget(exchangeName, string.Empty, out operation, out target);

    public static bool TryResolveTarget(string exchangeName, string routingKey, out string operation, out string target)
        => MessageSecurityPolicyRegistry.TryResolve(
            exchangeName, routingKey, MessageProtection.MlDsa, out operation, out target);

    public string GetExchangeType(string exchangeName) => _inner.GetExchangeType(exchangeName);
    public Task Shutdown() => _inner.Shutdown();
    public Task<ResultObj> ConnectAndSetUp() => _inner.ConnectAndSetUp();
    public Task<ResultObj> ConnectAndSetUp(CancellationToken cancellationToken) => _inner.ConnectAndSetUp(cancellationToken);
    public Task<ResultObj> ConnectAndSetUp(CancellationToken cancellationToken, int? maxRetriesOverride) => _inner.ConnectAndSetUp(cancellationToken, maxRetriesOverride);
    public Task<ResultObj> ShutdownRepo() => _inner.ShutdownRepo();
    public Task<string> PublishJsonZAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class => _inner.PublishJsonZAsync(exchangeName, obj, routingKey);
    public Task<string> PublishJsonZWithIDAsync<T>(string exchangeName, T obj, string id, string routingKey = "") where T : class => _inner.PublishJsonZWithIDAsync(exchangeName, obj, id, routingKey);
}
