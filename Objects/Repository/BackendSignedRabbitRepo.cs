using System;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Objects.Repository;

/// <summary>
/// The single signing decorator for trusted backend RabbitMQ publishers.
/// It signs processor commands and Data control messages, then delegates all
/// transport behavior to the ordinary RabbitRepo.
/// </summary>
public sealed class BackendSignedRabbitRepo : IRabbitRepo
{
    private static readonly string[] ProcessorOperations =
    {
        "getCmdProcessorSource", "getCmdProcessorHelp", "getCmdProcessorList",
        "deleteCmdProcessor", "addCmdProcessor", "processorCommand",
        "processorQueueDic", "processorScan", "cancelCommand",
        "getConnectSource", "getConnectList", "deleteConnect", "addConnect",
        "processorInit"
    };

    private static readonly string[] DataControlOperations =
    {
        "dataPurge", "fillUserTokens", "saveData", "processBlogList",
        "restorePingInfosForAllUsers", "initData", "callAgentFunction", "processorCustomConnectUpdate"
    };

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
        await SignIfRequiredAsync(exchangeName, obj).ConfigureAwait(false);
        await _inner.PublishAsync(exchangeName, obj, routingKey).ConfigureAwait(false);
    }

    public async Task PublishAsync(string exchangeName, object? obj, string routingKey = "")
    {
        if (obj == null && IsDataControlOperation(exchangeName))
        {
            var command = new BackendControlCommand();
            await SignIfRequiredAsync(exchangeName, command).ConfigureAwait(false);
            await _inner.PublishAsync(exchangeName, command, routingKey).ConfigureAwait(false);
            return;
        }

        await SignIfRequiredAsync(exchangeName, obj).ConfigureAwait(false);
        await _inner.PublishAsync(exchangeName, obj, routingKey).ConfigureAwait(false);
    }

    private async Task SignIfRequiredAsync(string exchangeName, object? obj)
    {
        if (obj is not IBackendSignedMessage message || !TryResolveTarget(exchangeName, out var operation, out var target)) return;
        message.BackendSignature = await _signatureService.SignAsync(operation, target, message).ConfigureAwait(false);
    }

    public static bool TryResolveTarget(string exchangeName, out string operation, out string target)
    {
        foreach (var candidate in DataControlOperations)
        {
            if (string.Equals(exchangeName, candidate, StringComparison.Ordinal))
            {
                operation = candidate;
                target = "data";
                return true;
            }
        }

        foreach (var candidate in ProcessorOperations)
        {
            if (exchangeName.StartsWith(candidate, StringComparison.Ordinal) && exchangeName.Length > candidate.Length)
            {
                operation = candidate;
                target = exchangeName[candidate.Length..];
                return true;
            }
        }

        operation = string.Empty;
        target = string.Empty;
        return false;
    }

    private static bool IsDataControlOperation(string exchangeName)
    {
        foreach (var operation in DataControlOperations)
        {
            if (string.Equals(exchangeName, operation, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public string GetExchangeType(string exchangeName) => _inner.GetExchangeType(exchangeName);
    public Task Shutdown() => _inner.Shutdown();
    public Task<ResultObj> ConnectAndSetUp() => _inner.ConnectAndSetUp();
    public Task<ResultObj> ConnectAndSetUp(CancellationToken cancellationToken) => _inner.ConnectAndSetUp(cancellationToken);
    public Task<ResultObj> ConnectAndSetUp(CancellationToken cancellationToken, int? maxRetriesOverride) => _inner.ConnectAndSetUp(cancellationToken, maxRetriesOverride);
    public Task<ResultObj> ShutdownRepo() => _inner.ShutdownRepo();
    public Task<string> PublishJsonZAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class => _inner.PublishJsonZAsync(exchangeName, obj, routingKey);
    public Task<string> PublishJsonZWithIDAsync<T>(string exchangeName, T obj, string id, string routingKey = "") where T : class => _inner.PublishJsonZWithIDAsync(exchangeName, obj, id, routingKey);
}
