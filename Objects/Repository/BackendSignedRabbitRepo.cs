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
    private readonly IProcessorCommandSigner? _processorSigner;

    public BackendSignedRabbitRepo(IRabbitRepo inner, IBackendMessageSignatureService signatureService,
        IProcessorCommandSigner? processorSigner = null)
    {
        _inner = inner;
        _signatureService = signatureService;
        _processorSigner = processorSigner;
    }

    public SystemUrl SystemUrl { get => _inner.SystemUrl; set => _inner.SystemUrl = value; }

    public async Task PublishAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class
    {
        if (await PublishProcessorProfileAsync(exchangeName, obj, routingKey)) return;
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
        if (await PublishProcessorProfileAsync(exchangeName, obj, routingKey)) return;
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

    private async Task<bool> PublishProcessorProfileAsync(string exchange, object? obj, string routingKey)
    {
        if (_processorSigner == null) return false;
        string operation, target;
        if (exchange == ProcessorRabbitTopology.CommandsExchange) {
            if (!ProcessorRabbitTopology.TryParseRoutingKey(routingKey, out target, out operation)) return false;
        } else {
            if (!TryResolveTarget(exchange, routingKey, out operation, out target) ||
                !ProcessorRabbitTopology.IsSupportedOperation(operation)) return false;
        }
        if (!MessageSecurityPolicyRegistry.RequiresProcessorSignature(operation)) return false;
        bool useEcdsa = _processorSigner.RequiresEcdsa(target);
        if (!useEcdsa && operation is "processorFirmwareUpdate" or "processorFirmwareHealthAck")
            throw new InvalidOperationException("ESP32 firmware commands require IsQuantumCapable=false in processor state.");
        if (!useEcdsa) return false;
        if (obj == null) throw new InvalidOperationException("Signed processor command requires a payload.");
        var envelope = _processorSigner.Sign(operation, target, obj);
        await _inner.PublishAsync(exchange, envelope, routingKey).ConfigureAwait(false);
        return true;
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
