using System;
using System.Collections.Generic;
using System.Linq;
using NetworkMonitor.Objects.Repository.Helpers;

namespace NetworkMonitor.Objects.Repository;

public enum MessageProtection
{
    MlDsa,
    BackendHmac,
    LlmHmac
}

public enum MessageRouteMatch
{
    Exact,
    Prefix,
    QueryIndexResult,
    Suffix
}

public sealed record MessageSecurityPolicy(
    string Operation,
    string Target,
    MessageProtection Protection,
    MessageRouteMatch RouteMatch = MessageRouteMatch.Exact,
    bool IsPayloadFree = false,
    bool WrapsEmailBatch = false,
    bool AllowsDynamicTarget = false);

/// <summary>
/// Authoritative security policy for protected RabbitMQ operations. Publishers
/// and consumers both consult this registry so their protection mechanisms
/// cannot silently drift apart.
/// </summary>
public static class MessageSecurityPolicyRegistry
{
    private static readonly MessageSecurityPolicy[] Policies =
    {
        // Commands addressed to one processor. Legacy exchanges append AppID;
        // v2 transports the same operation and target in the routing key.
        MlDsaPrefix("getCmdProcessorSource"), MlDsaPrefix("getCmdProcessorHelp"),
        MlDsaPrefix("getCmdProcessorList"), MlDsaPrefix("deleteCmdProcessor"),
        MlDsaPrefix("addCmdProcessor"), MlDsaPrefix("processorCommand"),
        MlDsaPrefix("processorQueueDic"), MlDsaPrefix("processorScan"),
        MlDsaPrefix("cancelCommand"), MlDsaPrefix("getConnectSource"),
        MlDsaPrefix("getConnectList"), MlDsaPrefix("deleteConnect"),
        MlDsaPrefix("addConnect"), MlDsaPrefix("processorInit"),

        // Data control operations.
        MlDsaExact("dataPurge", "data", payloadFree: true),
        MlDsaExact("fillUserTokens", "data", payloadFree: true),
        MlDsaExact("saveData", "data", payloadFree: true),
        MlDsaExact("processBlogList", "data", payloadFree: true),
        MlDsaExact("restorePingInfosForAllUsers", "data", payloadFree: true),
        MlDsaExact("initData", "data"),
        MlDsaExact("callAgentFunction", "data"),
        MlDsaExact("processorCustomConnectUpdate", "data"),

        // Shared processor state.
        MlDsaExact("addProcessor", "processor-state"),
        MlDsaExact("updateProcessor", "processor-state"),
        MlDsaExact("fullProcessorList", "processor-state"),

        // Administrative and alert operations.
        MlDsaExact("createHostSummaryReport", "data", payloadFree: true),
        MlDsaExact("sendHostReport", "alert"),
        MlDsaExact("sendGenericEmail", "alert"),
        MlDsaExact("userHostExpire", "alert", wrapsEmailBatch: true),
        MlDsaExact("userProcessorExpire", "alert", wrapsEmailBatch: true),
        MlDsaExact("userUpgrade", "alert", wrapsEmailBatch: true),

        // High-volume messages within the trusted backend HMAC domain.
        BackendHmac("updateUserSubscription"), BackendHmac("boostTokenForUser"),
        BackendHmac("updateUserCustomerId"), BackendHmac("paymentComplete"),
        BackendHmac("registerUser"), BackendHmac("updateProducts"),
        BackendHmac("updateUserPingInfos"), BackendHmac("pingInfosComplete"),
        BackendHmac("paymentWakeUp", payloadFree: true),
        BackendHmac("paymentCheck", payloadFree: true),
        BackendHmac("paymentServiceReady"), BackendHmac("alertServiceReady"),
        BackendHmac("monitorServiceReady"), BackendHmac("monitorDataReady"),
        BackendHmac("dataService_agentflow"), BackendHmac("createIndex"),
        BackendHmac("createSnapshot"), BackendHmac("mlCheck"),
        BackendHmac("mlCheckHost"), BackendHmac("mlCheckLatestHosts", payloadFree: true),
        BackendHmac("predictPingInfos"), BackendHmac("predictAlertFlag"),
        BackendHmac("predictAlertSent"), BackendHmac("predictResetAlerts"),
        BackendHmac("alertMessageResetPredictAlerts"),
        BackendHmac("alertUpdatePredictStatusAlerts"), BackendHmac("predictServiceReady"),
        BackendHmac("dataCheck"), BackendHmac("systemLlmOutput"),
        BackendHmac("systemLlmStarted"), BackendHmac("systemLlmStopped"),
        BackendHmac("monitorCheck", payloadFree: true),
        BackendHmac("getProducts", payloadFree: true),
        BackendHmac("refreshUsers", payloadFree: true),
        BackendHmac("saveDataToFile"), BackendHmac("systemLlmInput"),
        BackendHmac("systemLlmStart"), BackendHmac("systemLlmStop"),
        BackendHmac("serviceWakeUp", payloadFree: true),
        BackendHmac("monitorAlert", payloadFree: true),
        BackendHmac("predictAlert", payloadFree: true),
        BackendHmac("alertMessageInit"), BackendHmac("alertMessage"),
        BackendHmac("updateUserInfoAlertMessage"),

        // Messages in the separate LLM HMAC trust domain.
        LlmHmacExact("llmServiceFunction"), LlmHmacExact("llmServiceMessage"),
        LlmHmacExact("llmServiceTimeout"), LlmHmacExact("llmUpdateTokensUsed"),
        LlmHmacExact("llmServiceStarted"), LlmHmacExact("historyStore"),
        LlmHmacExact("functionRegistryReply"),
        LlmHmacExact("queryIndex", dynamicTarget: true),
        LlmHmacExact("queryMemory", dynamicTarget: true),
        LlmHmacExact("queryMemoryTurnWindow", dynamicTarget: true),
        LlmHmacExact("queryMemoryTurnRange", dynamicTarget: true),
        new("queryIndexResult", "queryIndexResult", MessageProtection.LlmHmac,
            MessageRouteMatch.QueryIndexResult, AllowsDynamicTarget: true),
        LlmHmacSuffix("MemoryQueryResult"),
        LlmHmacSuffix("MemoryTurnWindowResult"),
        LlmHmacSuffix("MemoryTurnRangeResult"),
        LlmHmacPrefix("llmStartSession"), LlmHmacPrefix("llmUserInput"),
        LlmHmacPrefix("llmStopRequest"), LlmHmacPrefix("llmRemoveSession"),
        LlmHmacPrefix("getFunctionRegistryFiltered", payloadFree: true),
        LlmHmacPrefix("getFunctionRegistry", payloadFree: true)
    };

    public static IReadOnlyList<MessageSecurityPolicy> All => Policies;

    public static bool TryResolve(
        string exchange,
        string routingKey,
        MessageProtection protection,
        out string operation,
        out string target)
    {
        if (protection == MessageProtection.MlDsa &&
            string.Equals(exchange, ProcessorRabbitTopology.CommandsExchange, StringComparison.Ordinal) &&
            ProcessorRabbitTopology.TryParseRoutingKey(routingKey, out var parsedTarget, out var parsedOperation))
        {
            var isProtected = Policies.Any(policy =>
                policy.Protection == protection &&
                string.Equals(policy.Operation, parsedOperation, StringComparison.Ordinal));
            operation = isProtected ? parsedOperation : string.Empty;
            target = isProtected ? parsedTarget : string.Empty;
            return isProtected;
        }

        foreach (var policy in Policies)
        {
            if (policy.Protection != protection) continue;
            if (policy.RouteMatch == MessageRouteMatch.Exact &&
                string.Equals(exchange, policy.Operation, StringComparison.Ordinal))
            {
                operation = policy.Operation;
                target = policy.Target;
                return true;
            }
            if (policy.RouteMatch == MessageRouteMatch.Prefix &&
                exchange.StartsWith(policy.Operation, StringComparison.Ordinal) &&
                exchange.Length > policy.Operation.Length)
            {
                operation = policy.Operation;
                target = exchange[policy.Operation.Length..];
                return true;
            }
            if (policy.RouteMatch == MessageRouteMatch.QueryIndexResult &&
                (exchange.EndsWith("QueryIndexResult", StringComparison.Ordinal) ||
                 exchange.StartsWith("queryIndexResult", StringComparison.Ordinal)))
            {
                operation = policy.Operation;
                target = policy.Target;
                return true;
            }
            if (policy.RouteMatch == MessageRouteMatch.Suffix &&
                exchange.EndsWith(policy.Operation, StringComparison.Ordinal) &&
                exchange.Length > policy.Operation.Length)
            {
                operation = policy.Operation;
                target = exchange[..^policy.Operation.Length];
                return true;
            }
        }

        operation = string.Empty;
        target = string.Empty;
        return false;
    }

    public static bool Requires(string operation, string target, MessageProtection protection)
    {
        return Policies.Any(policy =>
            policy.Protection == protection &&
            string.Equals(policy.Operation, operation, StringComparison.Ordinal) &&
            (policy.AllowsDynamicTarget || string.IsNullOrEmpty(policy.Target) ||
             string.Equals(policy.Target, target, StringComparison.Ordinal)));
    }

    public static bool IsPayloadFreeMlDsa(string operation) => Policies.Any(policy =>
        policy.Protection == MessageProtection.MlDsa && policy.IsPayloadFree &&
        string.Equals(policy.Operation, operation, StringComparison.Ordinal));

    public static bool IsPayloadFreeBackendHmac(string operation) => Policies.Any(policy =>
        policy.Protection == MessageProtection.BackendHmac && policy.IsPayloadFree &&
        string.Equals(policy.Operation, operation, StringComparison.Ordinal));

    public static bool IsPayloadFreeLlmHmac(string operation) => Policies.Any(policy =>
        policy.Protection == MessageProtection.LlmHmac && policy.IsPayloadFree &&
        string.Equals(policy.Operation, operation, StringComparison.Ordinal));

    public static bool WrapsEmailBatch(string operation) => Policies.Any(policy =>
        policy.Protection == MessageProtection.MlDsa && policy.WrapsEmailBatch &&
        string.Equals(policy.Operation, operation, StringComparison.Ordinal));

    private static MessageSecurityPolicy MlDsaPrefix(string operation) =>
        new(operation, string.Empty, MessageProtection.MlDsa, MessageRouteMatch.Prefix);

    private static MessageSecurityPolicy MlDsaExact(
        string operation,
        string target,
        bool payloadFree = false,
        bool wrapsEmailBatch = false) =>
        new(operation, target, MessageProtection.MlDsa, MessageRouteMatch.Exact, payloadFree, wrapsEmailBatch);

    private static MessageSecurityPolicy BackendHmac(string operation, bool payloadFree = false) =>
        new(operation, operation, MessageProtection.BackendHmac, IsPayloadFree: payloadFree);

    private static MessageSecurityPolicy LlmHmacExact(string operation, bool dynamicTarget = false) =>
        new(operation, operation, MessageProtection.LlmHmac, AllowsDynamicTarget: dynamicTarget);

    private static MessageSecurityPolicy LlmHmacPrefix(string operation, bool payloadFree = false) =>
        new(operation, string.Empty, MessageProtection.LlmHmac, MessageRouteMatch.Prefix,
            IsPayloadFree: payloadFree, AllowsDynamicTarget: true);

    private static MessageSecurityPolicy LlmHmacSuffix(string operation) =>
        new(operation, string.Empty, MessageProtection.LlmHmac, MessageRouteMatch.Suffix,
            AllowsDynamicTarget: true);
}
