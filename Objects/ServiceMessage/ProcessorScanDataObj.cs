
using NetworkMonitor.Utils;

namespace NetworkMonitor.Objects.ServiceMessage;
    public class ProcessorScanDataObj {
    private bool _useDefaultEndpoint;
    private string _defaultEndpoint="";
    private string _arguments="";
    private bool _argsEscaped=false;
    private string _scanCommandOutput="";
    private string _messageID="";
    private string _callingService="";
    private LLMServiceObj _llmServiceObj= new LLMServiceObj();
    private int _lineLimit = -1;
    private int _page = 1;
    private string _agentLocation="";
    private int _timeout = 600; // Default timeout for cmd processor operations
    private DateTime _startTime;
    private bool _sendMessage = true;
    private string _authKey="";
    private string _agentId="";
    private bool _isControllerCall=false;
    private string _type="";
    private bool _isAck=false;
    private bool _scanCommandSuccess;

    public bool UseDefaultEndpoint { get => _useDefaultEndpoint; set => _useDefaultEndpoint = value; }
    public string  DefaultEndpoint { get => _defaultEndpoint; set => _defaultEndpoint = value; }
    public string Arguments { get => _arguments; set => _arguments = value; }
    public string CallingService { get => _callingService; set => _callingService = value; }
    public string ScanCommandOutput { get => _scanCommandOutput; set => _scanCommandOutput = value; }
    public string MessageID { get => _messageID; set => _messageID = value; }
    public LLMServiceObj LlmServiceObj { get => _llmServiceObj; set => _llmServiceObj = value; }
    public int LineLimit { get => _lineLimit; set => _lineLimit = value; }
    public int Page { get => _page; set => _page = value; }
    public string AgentLocation { get => _agentLocation; set => _agentLocation = value; }
    public int TimeoutSeconds { get => _timeout; set => _timeout = value; }
    public DateTime StartTime { get => _startTime; set => _startTime = value; }
    public bool SendMessage { get => _sendMessage; set => _sendMessage = value; }
    public string AuthKey { get => _authKey; set => _authKey = value; }
    public string AgentID { get => _agentId; set => _agentId = value; }
    public bool IsControllerCall { get => _isControllerCall; set => _isControllerCall = value; }
    public string Type { get => _type; set => _type = value; }
    public string RootMessageID => _llmServiceObj.RootMessageID;

    public bool IsAck { get => _isAck; set => _isAck = value; }
    public bool ArgsEscaped { get => _argsEscaped; set => _argsEscaped = value; }
    public bool ScanCommandSuccess { get => _scanCommandSuccess; set => _scanCommandSuccess = value; }

    public ProcessorScanDataObj()
        {
            _messageID = StringUtils.GetNanoid();
            _startTime = DateTime.UtcNow;
        }
}