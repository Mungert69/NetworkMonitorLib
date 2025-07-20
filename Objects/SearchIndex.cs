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

public class QueryIndexRequest
{
    public string IndexName { get; set; } = "";
    public string QueryText { get; set; } = "";
    public string AppID { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public string MessageID { get; set; } = "";
    public string LLMRunnerType { get; set; } = "";
    public List<QueryResultObj> QueryResults { get; set; } = new();
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string RoutingKey { get; set; } = "";
}

public class QueryResultObj
{
    public string Input { get; set; }
    public string Output { get; set; }
}


    public class CreateSnapshotRequest
    {
        public string SnapshotRepo { get; set; } = "local_backup";
        public string SnapshotName { get; set; } = "";
        public string Indices { get; set; } = "documents,securitybooks";
    }



