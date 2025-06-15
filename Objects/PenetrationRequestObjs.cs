namespace NetworkMonitor.Objects;

 public class CancelFunctionsRequest
    {
        public string MessageId { get; set; }= string.Empty;
    }

    public class GetCmdProcessorListRequest
    {
        public string AgentLocation { get; set; }= string.Empty;
    }

    public class GetCmdProcessorHelpRequest
    {
        public string CmdProcessorType { get; set; }= string.Empty;
        public string AgentLocation { get; set; }= string.Empty;
    }

    public class GetCmdProcessorSourceCodeRequest
    {
        public string CmdProcessorType { get; set; }= string.Empty;
        public string AgentLocation { get; set; }= string.Empty;
    }

    public class AddCmdProcessorRequest
    {
        public string CmdProcessorType { get; set; }= string.Empty;
        public string SourceCode { get; set; }= string.Empty;
        public string AgentLocation { get; set; }= string.Empty;
    }

    public class RunCmdProcessorRequest
    {
        public string CmdProcessorType { get; set; }= string.Empty;
        public string Arguments { get; set; }= string.Empty;
        public string AgentLocation { get; set; }= string.Empty;
         public int NumberLines { get; set; } 
        public int Page {get; set; } = 1;
    }

    public class DeleteCmdProcessorRequest
    {
        public string CmdProcessorType { get; set; }= string.Empty;
        public string AgentLocation { get; set; }= string.Empty;
    }

    public class SearchMetasploitModulesRequest
    {
        public string ModuleType { get; set; }= string.Empty;
        public string Platform { get; set; }= string.Empty;
        public string Architecture { get; set; }= string.Empty;
        public string Cve { get; set; }= string.Empty;
        public string Edb { get; set; }= string.Empty;
        public string Rank { get; set; }= string.Empty;
        public string Keywords { get; set; }= string.Empty;
        public int NumberLines { get; set; }
        public int Page { get; set; }
    }

    public class GetMetasploitModuleInfoRequest
    {
        public string ModuleName { get; set; }= string.Empty;
    }
public class IsFunctionCompletedRequest { 
    public string MessageID { get; set; }= string.Empty;
}
public class NmapRequest
{
    public string ScanOptions { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? AgentLocation { get; set; }
    public int NumberLines { get; set; } = 100; // Default value
    public int Page { get; set; } = 1; // Default value
}

public class OpensslRequest
{
    public string CommandOptions { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? AgentLocation { get; set; }
    public int NumberLines { get; set; } = 100; // Default value
    public int Page { get; set; } = 1; // Default value
}

public class BusyboxRequest
{
    public string Command { get; set; } = string.Empty;
    public string? AgentLocation { get; set; }
    public int NumberLines { get; set; } = 10; // Default value
    public int Page { get; set; } = 1; // Default value
}

public class MetasploitRequest
{
    public string ModuleName { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? AgentLocation { get; set; }
    public int NumberLines { get; set; } = -1; // Default value
    public int Page { get; set; } = 1; // Default value
}
