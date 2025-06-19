namespace NetworkMonitor.Objects;
public class MLParams
{
    private int _predictWindow = 300;
    private int _spikeDetectionThreshold = 5;
    private double _changeConfidence = 90;
    private double _spikeConfidence = 99;
    private int _changePreTrain = 50;
    private int _spikePreTrain = 50;
    private string _llmModelPath = "";
    private string _llmModelFileName = "";
    private string _llmContextFileName = "";
    private string _llmSystemPrompt = "";
    private string _llmVersion="";
    private int _llmThreads = 4;
    private int _llmSystemPromptTimeout = 10;
    private int _llmUserPromptTimeout = 50;
    private int _llmSessionIdleTimeout = 60;
    private string _llmReversePrompt="";
    private string _llmPromptMode = "-if -sp";
    private string _llmTemp="0.0";
    private int _llmCtxSize=12000;
    private int _llmOpenAICtxSize=32000;
    private int _llmResponseTokens=4000;
    private int _llmPromptTokens = 28000;
    private int _llmCtxRatio=6;
    private string _llmStartName = "monitor";
    private string _llmRunnerType="TurboLLM";
    private bool _llmNoInitMessage=false;
    private bool _llmNoThink=false;
    private bool _startThisTestLLM = true;
    private string _llmGptModel = "gpt-4o-mini";
    private string _llmHFModelID = "";
    private string _llmHFKey ="";
    private string _llmHFUrl="";
    private bool _llmUseHF;
    private bool _isStream;
    private string _llmHFModelVersion;
    private bool _llmReportProcess =false;
    private bool _xmlFunctionParsing=false;
    private string _bertModelDir="stsb-bert-tiny-onnx";
    private int _bertModelVecDim=128;
    private string _openSearchKey="";
    private string _openSearchUser="admin";
    private string _openSearchDefaultIndex="documents";
    private string _openSearchUrl="";
    private string _openAIApiKey="Missing";
    private string _dataRepoId="";
    private string _hFToken="";
    private bool _addSystemRag=false;
     private bool _addFunctionRag=false;
     private Dictionary<string, string> _llmRunnerRoutingKeys = new();

    private Dictionary<string, string> _llmFunctionDic = new Dictionary<string, string>();

    public int PredictWindow { get => _predictWindow; set => _predictWindow = value; }
    public int SpikeDetectionThreshold { get => _spikeDetectionThreshold; set => _spikeDetectionThreshold = value; }
    public double ChangeConfidence { get => _changeConfidence; set => _changeConfidence = value; }
    public int ChangePreTrain { get => _changePreTrain; set => _changePreTrain = value; }
    public int SpikePreTrain { get => _spikePreTrain; set => _spikePreTrain = value; }
    public double SpikeConfidence { get => _spikeConfidence; set => _spikeConfidence = value; }
    public string LlmModelPath { get => _llmModelPath; set => _llmModelPath = value; }
    public string LlmModelFileName { get => _llmModelFileName; set => _llmModelFileName = value; }
    public string LlmContextFileName { get => _llmContextFileName; set => _llmContextFileName = value; }
    public string LlmSystemPrompt { get => _llmSystemPrompt; set => _llmSystemPrompt = value; }
    public int LlmThreads { get => _llmThreads; set => _llmThreads = value; }
    public int LlmSystemPromptTimeout { get => _llmSystemPromptTimeout; set => _llmSystemPromptTimeout = value; }
    public int LlmUserPromptTimeout { get => _llmUserPromptTimeout; set => _llmUserPromptTimeout = value; }
    public int LlmSessionIdleTimeout { get => _llmSessionIdleTimeout; set => _llmSessionIdleTimeout = value; }
       public string LlmReversePrompt { get => _llmReversePrompt; set => _llmReversePrompt = value; }
    public string LlmPromptMode { get => _llmPromptMode; set => _llmPromptMode = value; }
    public Dictionary<string,string> LlmFunctionDic { get => _llmFunctionDic; set => _llmFunctionDic = value; }
    public int LlmCtxSize { get => _llmCtxSize; set => _llmCtxSize = value; }
     public int LlmResponseTokens { get => _llmResponseTokens; set => _llmResponseTokens = value; }
    public int LlmPromptTokens { get => _llmPromptTokens; set => _llmPromptTokens = value; }
    public string LlmVersion { get => _llmVersion; set => _llmVersion = value; }
    public string LlmStartName { get => _llmStartName; set => _llmStartName = value; }
    public int LlmOpenAICtxSize { get => _llmOpenAICtxSize; set => _llmOpenAICtxSize = value; }
    public bool StartThisTestLLM { get => _startThisTestLLM; set => _startThisTestLLM = value; }
    public string LlmGptModel { get => _llmGptModel; set => _llmGptModel = value; }
    public bool LlmNoInitMessage { get => _llmNoInitMessage; set => _llmNoInitMessage = value; }
    public bool LlmReportProcess { get => _llmReportProcess; set => _llmReportProcess = value; }
    public string LlmRunnerType { get => _llmRunnerType; set => _llmRunnerType = value; }
    public string LlmTemp { get => _llmTemp; set => _llmTemp = value; }
    public bool XmlFunctionParsing { get => _xmlFunctionParsing; set => _xmlFunctionParsing = value; }
    public string LlmHFModelID { get => _llmHFModelID; set => _llmHFModelID = value; }
    public string LlmHFKey { get => _llmHFKey; set => _llmHFKey = value; }
    public string LlmHFUrl { get => _llmHFUrl; set => _llmHFUrl = value; }
    public bool LlmUseHF { get => _llmUseHF; set => _llmUseHF = value; }
    public string LlmHFModelVersion { get => _llmHFModelVersion; set => _llmHFModelVersion = value; }
    public int LlmCtxRatio { get => _llmCtxRatio; set => _llmCtxRatio = value; }
    public bool IsStream { get => _isStream; set => _isStream = value; }
    public string BertModelDir { get => _bertModelDir; set => _bertModelDir = value; }
    public int BertModelVecDim { get => _bertModelVecDim; set => _bertModelVecDim = value; }
    public bool AddSystemRag { get => _addSystemRag; set => _addSystemRag = value; }
    public bool AddFunctionRag { get => _addFunctionRag; set => _addFunctionRag = value; }
    public string OpenSearchKey { get => _openSearchKey; set => _openSearchKey = value; }
    public string OpenSearchUser { get => _openSearchUser; set => _openSearchUser = value; }
    public string OpenSearchDefaultIndex { get => _openSearchDefaultIndex; set => _openSearchDefaultIndex = value; }
    public string OpenSearchUrl { get => _openSearchUrl; set => _openSearchUrl = value; }
    public string OpenAIApiKey { get => _openAIApiKey; set => _openAIApiKey = value; }
    public string DataRepoId { get => _dataRepoId; set => _dataRepoId = value; }
    public string HFToken { get => _hFToken; set => _hFToken = value; }
    public bool LlmNoThink { get => _llmNoThink; set => _llmNoThink = value; }
    public Dictionary<global::System.String, global::System.String> LlmRunnerRoutingKeys { get => _llmRunnerRoutingKeys; set => _llmRunnerRoutingKeys = value; }
}
