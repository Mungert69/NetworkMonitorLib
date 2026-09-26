using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NetworkMonitor.Objects.Repository.Helpers
{
    /// <summary>
    /// Dormant v2 ProcessorAgent RabbitMQ topology definitions.
    /// Legacy processors continue to use one exchange per operation and AppID.
    /// </summary>
    public static class ProcessorRabbitTopology
    {
        public const int LegacyVersion = 1;
        public const int Version = 2;
        public const string CommandsExchange = "monitorProcessor.commands.v2";
        public const string QueuePrefix = "monitorProcessor.queue.";
        public const int MaxRoutingIdLength = 160;

        private static readonly HashSet<string> SupportedOperations = new(StringComparer.Ordinal)
        {
            "processorConnect",
            "removePingInfos",
            "processorInit",
            "processorFirmwareUpdate",
            "processorFirmwareHealthAck",
            "processorAlertFlag",
            "processorAlertSent",
            "processorQueueDic",
            "processorResetAlerts",
            "processorWakeUp",
            "processorAuthKey",
            "processorUserEvent",
            "refreshAuthKey",
            "processorScan",
            "processorCommand",
            "cancelCommand",
            "addCmdProcessor",
            "deleteCmdProcessor",
            "getCmdProcessorHelp",
            "getCmdProcessorList",
            "getCmdProcessorSource",
            "addConnect",
            "deleteConnect",
            "getConnectList",
            "getConnectSource"
        };

        public static bool IsSupportedOperation(string? operation)
        {
            return operation != null && SupportedOperations.Contains(operation);
        }

        public static int NormalizeVersion(int version)
        {
            return version == Version ? Version : LegacyVersion;
        }

        public static bool IsValidRoutingId(string? routingId)
        {
            if (string.IsNullOrEmpty(routingId) || routingId.Length > MaxRoutingIdLength)
            {
                return false;
            }

            foreach (char value in routingId)
            {
                bool isAsciiLetter = value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
                bool isDigit = value is >= '0' and <= '9';
                if (!isAsciiLetter && !isDigit && value != '-' && value != '_')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Derives a RabbitMQ-safe address for a processor. User-owned AppIDs
        /// conventionally start with the FusionAuth UUID followed by a hyphen.
        /// Preserve that safe owner prefix for OAuth scope authorization while
        /// hashing the complete AppID so the remaining identity is never used
        /// directly as a RabbitMQ resource name. Other AppIDs use an opaque
        /// hash-only route.
        /// </summary>
        public static string GetRoutingId(string appId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(appId);
            string appIdHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(appId))).ToLowerInvariant();
            if (TryGetFusionAuthUserIdPrefix(appId, out string userId))
            {
                return $"u_{userId}_p_{appIdHash}";
            }

            return "p_" + appIdHash;
        }

        private static bool TryGetFusionAuthUserIdPrefix(string appId, out string userId)
        {
            userId = string.Empty;
            const int GuidLength = 36;
            if (appId.Length < GuidLength ||
                (appId.Length > GuidLength && appId[GuidLength] != '-') ||
                !Guid.TryParseExact(appId[..GuidLength], "D", out Guid parsedUserId))
            {
                return false;
            }

            userId = parsedUserId.ToString("D");
            return true;
        }

        public static string BuildRoutingKey(string routingId, string operation)
        {
            Validate(routingId, operation);
            return $"{routingId}.{operation}";
        }

        public static string BuildQueueName(string routingId, string operation)
        {
            Validate(routingId, operation);
            return $"{QueuePrefix}{routingId}.{operation}";
        }

        public static bool TryParseRoutingKey(
            string? routingKey,
            out string routingId,
            out string operation)
        {
            routingId = string.Empty;
            operation = string.Empty;

            if (string.IsNullOrEmpty(routingKey))
            {
                return false;
            }

            int separatorIndex = routingKey.LastIndexOf('.');
            if (separatorIndex <= 0 || separatorIndex == routingKey.Length - 1)
            {
                return false;
            }

            string candidateRoutingId = routingKey[..separatorIndex];
            string candidateOperation = routingKey[(separatorIndex + 1)..];
            if (!IsValidRoutingId(candidateRoutingId) || !IsSupportedOperation(candidateOperation))
            {
                return false;
            }

            routingId = candidateRoutingId;
            operation = candidateOperation;
            return true;
        }

        private static void Validate(string routingId, string operation)
        {
            if (!IsValidRoutingId(routingId))
            {
                throw new ArgumentException(
                    $"Processor routing ID must contain 1-{MaxRoutingIdLength} ASCII letters, digits, '-' or '_'.",
                    nameof(routingId));
            }

            if (!IsSupportedOperation(operation))
            {
                throw new ArgumentException("Unsupported processor RabbitMQ operation.", nameof(operation));
            }
        }
    }
}
