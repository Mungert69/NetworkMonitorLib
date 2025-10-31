using System;
using System.Collections.Generic;

namespace NetworkMonitor.Objects;

public class CreateIndexRequest
{
    public string IndexName { get; set; } = "";
    public string JsonMapping { get; set; } = "";
    public string JsonFile { get; set; } = "";
    public bool RecreateIndex { get; set; } = false;
    public string AppID { get; set; } = "";
    public string AuthKey { get; set; } = "";

    public string MessageID { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public bool CreateFromJsonDataDir { get; set; } = false;
}
public enum VectorSearchMode
{
    content,     // default
    question,
    summary
}

public static class VectorSearchModeHelper
{
    public static VectorSearchMode Parse(string value)
    {
        if (Enum.TryParse<VectorSearchMode>(value, true, out var mode))
            return mode;
        return VectorSearchMode.content;
    }

    public static string ToString(VectorSearchMode mode)
    {
        return mode.ToString();
    }

    public static bool IsValid(string value)
    {
        return Enum.TryParse<VectorSearchMode>(value, true, out _);
    }

    public static IEnumerable<string> GetAllModes()
    {
        return Enum.GetNames(typeof(VectorSearchMode));
    }
}
public class QueryIndexRequest
{
    public string IndexName { get; set; } = "";
    public VectorSearchMode VectorSearchMode { get; set; } = VectorSearchMode.content;
    public string QueryText { get; set; } = "";
    public string AppID { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public string MessageID { get; set; } = "";
    public string LLMRunnerType { get; set; } = "";
    public string ResponseExchange { get; set; } = "";
    public List<QueryResultObj> QueryResults { get; set; } = new();
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string RoutingKey { get; set; } = "";

    // String property for easy setting
   public void SetVectorSearchModeFromString(string value)
    {
        VectorSearchMode = VectorSearchModeHelper.Parse(value);
    }
}

public class QueryResultObj
{
    public string Input { get; set; }
    public string Output { get; set; }
    public float Score { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


public class CreateSnapshotRequest
{
    public string SnapshotRepo { get; set; } = "local_backup";
    public string SnapshotName { get; set; } = "";
    public string Indices { get; set; } = "documents,mitre,securitybooks,blogs";
}
