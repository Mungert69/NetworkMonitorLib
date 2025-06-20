namespace NetworkMonitor.Objects;
public class CreateIndexRequest
{
    public string IndexName { get; set; } = "";
    public string JsonMapping { get; set; } = "";
    public string JsonFile {get;set;} ="";
    public bool RecreateIndex { get; set; } = false;
    public string AppID { get; set; } = "";
    public string AuthKey { get; set; } = "";

    public string MessageID { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class QueryIndexRequest
{
    public string IndexName { get; set; } = "";
    public string QueryText { get; set; } = "";
    public string AppID { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public string MessageID { get; set; } = "";
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

