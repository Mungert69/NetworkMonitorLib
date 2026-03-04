using System;
using System.Collections.Generic;

namespace NetworkMonitor.Objects;

public class MemoryQueryRequest
{
    public string QueryText { get; set; } = "";
    public string UserId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int TopK { get; set; } = 8;
    public bool IncludeToolTurns { get; set; } = false;

    public string AppID { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public string MessageID { get; set; } = "";
    public string LLMRunnerType { get; set; } = "";
    public string ResponseExchange { get; set; } = "";
    public string RoutingKey { get; set; } = "";

    public List<MemoryQueryResult> Results { get; set; } = new();
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class MemoryQueryResult
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public int TurnIndex { get; set; }
    public long TurnUnixTime { get; set; }
    public float Score { get; set; }
    public string WhyMatched { get; set; } = "";
    public string ConfidenceBand { get; set; } = "";
    public string QueryIntent { get; set; } = "";
    public string SourceScope { get; set; } = "";
    public List<MemoryContextTurn> ContextBefore { get; set; } = new();
    public List<MemoryContextTurn> ContextAfter { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class MemoryContextTurn
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public int TurnIndex { get; set; }
    public long TurnUnixTime { get; set; }
}

public class MemoryTurnWindowRequest
{
    public string SessionId { get; set; } = "";
    public int TurnIndex { get; set; }
    public int WidthBefore { get; set; } = 2;
    public int WidthAfter { get; set; } = 2;
    public string UserId { get; set; } = "";

    public string AppID { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public string MessageID { get; set; } = "";
    public string LLMRunnerType { get; set; } = "";
    public string ResponseExchange { get; set; } = "";
    public string RoutingKey { get; set; } = "";

    public List<MemoryContextTurn> Turns { get; set; } = new();
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
