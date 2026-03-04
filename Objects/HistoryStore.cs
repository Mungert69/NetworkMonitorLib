using System.Collections.Generic;

namespace NetworkMonitor.Objects;

public enum HistoryStoreOperation
{
    upsert,
    get,
    delete,
    list
}

public class HistoryStoreRequest
{
    public HistoryStoreOperation Operation { get; set; } = HistoryStoreOperation.upsert;
    public string AppID { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public long StartUnixTime { get; set; }
    public string Name { get; set; } = "";
    public string LlmType { get; set; } = "";
    public string HistoryJson { get; set; } = "";
    public string MessageID { get; set; } = "";
    public string ResponseExchange { get; set; } = "";
    public string RoutingKey { get; set; } = "";
    public int Limit { get; set; } = 100;
}

public class HistoryStoreResultItem
{
    public string ServiceId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public long StartUnixTime { get; set; }
    public string Name { get; set; } = "";
    public string LlmType { get; set; } = "";
    public string HistoryJson { get; set; } = "";
}

public class HistoryStoreResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string MessageID { get; set; } = "";
    public HistoryStoreResultItem? Item { get; set; }
    public List<HistoryStoreResultItem> Items { get; set; } = new();
}
