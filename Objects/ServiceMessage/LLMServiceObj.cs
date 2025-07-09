
using NetworkMonitor.Objects;
using NetworkMonitor.Utils;
namespace NetworkMonitor.Objects.ServiceMessage
{
    public class LLMServiceObj
    {
#pragma warning disable CS8618
        public LLMServiceObj()
        {
            functionState = new FunctionState(); // Initialize FunctionState instance

            LlmStack = new Stack<string>();
            FunctionCallIdStack = new Stack<string>(); // Initialize the function call ID stack
            FunctionNameStack = new Stack<string>(); // Initialize the function name stack
            MessageIDStack = new Stack<string>(); // Initialize the function name stack
            IsProcessedStack = new Stack<bool>(); // Initialize the IsProcessed stack
            MessageID = StringUtils.GetNanoid();
            StartTimeUTC = DateTime.UtcNow;
        }

        public LLMServiceObj(LLMServiceObj other)
        {
            if (other != null) Copy(other);
        }
        public LLMServiceObj(LLMServiceObj other, Action<FunctionState> configureFunctionState) : this(other)
        {

            configureFunctionState?.Invoke(functionState);
        }

        public LLMServiceObj(Action<FunctionState> configureFunctionState)
        {
            functionState = new FunctionState();
            configureFunctionState?.Invoke(functionState);
            LlmStack = new Stack<string>();
            FunctionCallIdStack = new Stack<string>();
            FunctionNameStack = new Stack<string>();
            MessageIDStack = new Stack<string>();
            IsProcessedStack = new Stack<bool>();
            MessageID = StringUtils.GetNanoid();
            StartTimeUTC = DateTime.UtcNow;

        }
#pragma warning restore     CS8618
        // Copy constructor
        public void Copy(LLMServiceObj other)
        {
            // Copying primitive and reference type fields
            requestSessionId = other.requestSessionId;
            sessionId = other.sessionId;
            userInput = other.userInput;
            isUserLoggedIn = other.isUserLoggedIn;
            jsonFunction = other.jsonFunction;
            // Copy FunctionState
            functionState = new FunctionState();
            functionState.SetFunctionState(
                other.IsFunctionCall,
                other.IsFunctionCallResponse,
                other.IsFunctionCallError,
                other.IsFunctionCallStatus,
                other.IsFunctionStillRunning
            );
            functionName = other.functionName;
            functionCallId = other.FunctionCallId;
            llmRunnerType = other.llmRunnerType;
            sourceLlm = other.sourceLlm;
            destinationLlm = other.destinationLlm;
            tokensUsed = other.tokensUsed;
            llmMessage = other.llmMessage;
            isSystemLlm = other.IsSystemLlm;
            resultSuccess = other.resultSuccess;
            resultMessage = other.resultMessage;
            timeZone = other.timeZone;
            // Deep copy UserInfo and FuncationCallData
            functionCallData = new FunctionCallData(other.functionCallData);
            userInfo = new UserInfo(other.userInfo);
            // Deep copy of the stacks
            LlmStack = new Stack<string>(other.LlmStack);
            FunctionCallIdStack = new Stack<string>(other.FunctionCallIdStack);
            FunctionNameStack = new Stack<string>(other.FunctionNameStack);
            MessageIDStack = new Stack<string>(other.MessageIDStack);
            IsProcessedStack = new Stack<bool>(other.IsProcessedStack);
            LlmSessionStartName = other.LlmSessionStartName;
            messageID = other.MessageID;
            StartTimeUTC = other.StartTimeUTC;
            ChatAgentLocation = other.ChatAgentLocation;
            ToolsDefinitionId = other.ToolsDefinitionId;
            JsonToolsBuilderSpec = other.JsonToolsBuilderSpec;
            Timeout = other.Timeout;

        }
        private string messageID = "";
        private string requestSessionId = "";
        private string sessionId = "";
        private string userInput = "";
        private bool isUserLoggedIn;
        private string jsonFunction = "";
        private FunctionState functionState;
        private string functionName = "";
        private string swapFunctionName = "";
        private string functionCallId = "";
        private int? timeout =null;
        private string? toolsDefinitionId = null;
        private string? jsonToolsBuilderSpec = null;
        private string llmRunnerType = "TurboLLM";
        private string chatAgentLocation="";
        private string sourceLlm = "";
        private string destinationLlm = "";
        private int tokensUsed = 0;
        private DateTime startTimeUTC;
        private Stack<string> llmStack;  // Stack to store LLM names
        private Stack<string> functionCallIdStack;  // Stack to store Function Call IDs
        private Stack<string> functionNameStack;  // Stack to store Function Names
        private Stack<bool> isProcessedStack; // Stack to manage IsProcessed states

        private Stack<string> messageIDStack;  // Stack to store MessageIDs

        private string llmMessage = "";
        private bool resultSuccess;
        private string resultMessage = "";
        private string timeZone = "";
        private string llmSessionStartName = "";
        private FunctionCallData functionCallData;
        private UserInfo userInfo;
        private bool isFuncAck;
        private bool isSystemLlm = false;

        public string SessionId { get => sessionId; set => sessionId = value; }
        public string JsonFunction { get => jsonFunction; set => jsonFunction = value; }
        public string LlmMessage { get => llmMessage; set => llmMessage = value; }
        public bool ResultSuccess { get => resultSuccess; set => resultSuccess = value; }
        public string ResultMessage { get => resultMessage; set => resultMessage = value; }
        public string UserInput { get => userInput; set => userInput = value; }
        public string GetFunctionStateString() => functionState.StatesString();
        public void SetAsCall() => functionState.SetAsCall();
        public void SetAsCallError() => functionState.SetAsCall();
        public void SetAsNotCall() => functionState.SetAsNotCall();
        public void SetAsResponseComplete() => functionState.SetAsResponseComplete();
        public void SetOnlyResponse() => functionState.SetOnlyResponse();
        public void SetAsResponseRunning() => functionState.SetAsResponseRunning();
        public void SetAsResponseStatus() => functionState.SetAsResponseStatus();
        public void SetAsResponseStatusOnly() => functionState.SetAsResponseStatusOnly();
        public void SetAsResponseError() => functionState.SetAsResponseError();
        public void SetAsResponseErrorComplete() => functionState.SetAsResponseErrorComplete();
        public void SetFunctionState(bool functionCall, bool functionCallResponse, bool functionCallError, bool functionCallStatus, bool functionStillRunning) => functionState.SetFunctionState(functionCall, functionCallResponse, functionCallError, functionCallStatus, functionStillRunning);
        public bool IsFunctionCall
        {
            get => functionState.IsFunctionCall;
            set => functionState.IsFunctionCall = value;
        }

        public bool IsFunctionCallResponse
        {
            get => functionState.IsFunctionCallResponse;
            set => functionState.IsFunctionCallResponse = value;
        }

        public bool IsFunctionCallError
        {
            get => functionState.IsFunctionCallError;
            set => functionState.IsFunctionCallError = value;
        }

        public bool IsFunctionCallStatus
        {
            get => functionState.IsFunctionCallStatus;
            set => functionState.IsFunctionCallStatus = value;
        }

        public bool IsFunctionStillRunning
        {
            get => functionState.IsFunctionStillRunning;
            set => functionState.IsFunctionStillRunning = value;
        }

        public string RequestSessionId { get => requestSessionId; set => requestSessionId = value; }
        public FunctionCallData FunctionCallData { get => functionCallData; set => functionCallData = value; }
        public string FunctionName { get => functionName; set => functionName = value; }
        public string TimeZone { get => timeZone; set => timeZone = value; }
        public string LLMRunnerType { get => llmRunnerType; set => llmRunnerType = value; }
        public int TokensUsed { get => tokensUsed; set => tokensUsed = value; }
        public bool IsUserLoggedIn { get => isUserLoggedIn; set => isUserLoggedIn = value; }
        public UserInfo UserInfo { get => userInfo; set => userInfo = value; }
        public string SourceLlm { get => sourceLlm; set => sourceLlm = value; }
        public string DestinationLlm { get => destinationLlm; set => destinationLlm = value; }
        // Pop the last LLM name from the stack and set it to SourceLlm
        public void PopLlm()
        {
            if (LlmStack.Count > 0)
            {
                SourceLlm = LlmStack.Pop();
            }
            DestinationLlm = SourceLlm;
            PopMessageID();
            PopFunctionCallId();
            PopFunctionName();
            PopIsProcessed();
        }

        // Push the current SourceLlm to the stack and update DestinationLlm with the new LLM name
        public void PushLmm(string llmName, string newFunctionCallId, string newFunctionName, string newMessageID, bool newIsProcessed)
        {
            if (!string.IsNullOrEmpty(SourceLlm))
            {
                LlmStack.Push(SourceLlm);
            }
            SourceLlm = DestinationLlm;
            DestinationLlm = llmName;
            PushMessageID(newMessageID);
            PushFunctionCallId(newFunctionCallId);
            PushFunctionName(newFunctionName);
            PushIsProcessed(newIsProcessed);
        }


        public string LlmChainStartName
        {
            get
            {
                if (LlmStack.Count == 0)
                {
                    return SourceLlm;
                }
                else
                {
                    return LlmStack.ToArray()[0];
                }
            }
        }

        public string RootMessageID
        {
            get
            {
                if (MessageIDStack.Count == 0)
                {
                    return MessageID;
                }
                else
                {
                    return MessageIDStack.ToArray()[0];
                }
            }
        }


        public string FirstFunctionName
        {
            get
            {
                if (FunctionNameStack.Count == 0)
                {
                    return FunctionName;
                }
                else
                {
                    return FunctionNameStack.ToArray()[0];
                }
            }
        }


        public bool IsPrimaryLlm
        {
            get
            {
                if (isSystemLlm) return false;
                else
                    return SourceLlm == DestinationLlm;
            }
        }

        public string FunctionCallId { get => functionCallId; set => functionCallId = value; }
        public Stack<string> LlmStack { get => llmStack; set => llmStack = value; }
        public Stack<string> FunctionCallIdStack { get => functionCallIdStack; set => functionCallIdStack = value; }
        public Stack<string> FunctionNameStack { get => functionNameStack; set => functionNameStack = value; }
        public Stack<string> MessageIDStack { get => messageIDStack; set => messageIDStack = value; }

        public string MessageID { get => messageID; set => messageID = value; }
        public string LlmSessionStartName { get => llmSessionStartName; set => llmSessionStartName = value; }
        public bool IsFuncAck { get => isFuncAck; set => isFuncAck = value; }
        public bool IsProcessed { get; set; } // The current state of processing
        public Stack<bool> IsProcessedStack { get => isProcessedStack; set => isProcessedStack = value; }
        public bool IsSystemLlm { get => isSystemLlm; set => isSystemLlm = value; }
        public DateTime StartTimeUTC { get => startTimeUTC; set => startTimeUTC = value; }
        public string ChatAgentLocation { get => chatAgentLocation; set => chatAgentLocation = value; }
        public string? ToolsDefinitionId { get => toolsDefinitionId; set => toolsDefinitionId = value; }
        public string? JsonToolsBuilderSpec { get => jsonToolsBuilderSpec; set => jsonToolsBuilderSpec = value; }
        public string SwapFunctionName { get => swapFunctionName; set => swapFunctionName = value; }
        public int? Timeout { get => timeout; set => timeout = value; }

        public void PopMessageID()
        {
            if (MessageIDStack.Count > 0)
            {
                MessageID = MessageIDStack.Pop();
            }
        }
        public void PushMessageID(string newMessageID)
        {
            if (!string.IsNullOrEmpty(MessageID))
            {
                MessageIDStack.Push(MessageID);
            }
            MessageID = newMessageID;
        }
        // Pop the last Function Call ID from the stack and set it to FunctionCallId
        public void PopFunctionCallId()
        {
            if (FunctionCallIdStack.Count > 0)
            {
                FunctionCallId = FunctionCallIdStack.Pop();
            }
        }


        // Push the current FunctionCallId to the stack and update it with the new Function Call ID
        public void PushFunctionCallId(string newFunctionCallId)
        {
            if (!string.IsNullOrEmpty(FunctionCallId))
            {
                FunctionCallIdStack.Push(FunctionCallId);
            }
            FunctionCallId = newFunctionCallId;
        }
        public void PopFunctionName()
        {
            if (FunctionNameStack.Count > 0)
            {
                FunctionName = FunctionNameStack.Pop();
            }
        }

        // Push the current FunctionName to the stack and update it with the new Function Name
        public void PushFunctionName(string newFunctionName)
        {
            if (!string.IsNullOrEmpty(FunctionName))
            {
                FunctionNameStack.Push(FunctionName);
            }
            FunctionName = newFunctionName;
        }

        public void PushIsProcessed(bool newIsProcessed)
        {
            IsProcessedStack.Push(IsProcessed);
            IsProcessed = newIsProcessed;
        }

        // Pop the last IsProcessed state from the stack
        public void PopIsProcessed()
        {
            if (IsProcessedStack.Count > 0)
            {
                IsProcessed = IsProcessedStack.Pop();
            }
        }

        public DateTime GetClientCurrentTime()
        {
            try
            {
                var timeZoneInfo = timeZone != null ? TimeZoneInfo.FindSystemTimeZoneById(timeZone) : TimeZoneInfo.Utc;
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        public DateTime GetClientStartTime()
        {
            try
            {
                var timeZoneInfo = !string.IsNullOrEmpty(timeZone)  ? TimeZoneInfo.FindSystemTimeZoneById(timeZone) : TimeZoneInfo.Utc;
                return TimeZoneInfo.ConvertTimeFromUtc(startTimeUTC, timeZoneInfo);
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        public long GetClientCurrentUnixTime()
        {
            try
            {
                var timeZoneInfo = timeZone != null ? TimeZoneInfo.FindSystemTimeZoneById(timeZone) : TimeZoneInfo.Utc;
                DateTime clientTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
                return new DateTimeOffset(clientTime).ToUnixTimeSeconds();
            }
            catch
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
        }

        public long GetClientStartUnixTime()
        {
            try
            {
                var timeZoneInfo = !string.IsNullOrEmpty(timeZone) ? TimeZoneInfo.FindSystemTimeZoneById(timeZone) : TimeZoneInfo.Utc;
                DateTime clientStartTime = TimeZoneInfo.ConvertTimeFromUtc(startTimeUTC, timeZoneInfo);
                return new DateTimeOffset(clientStartTime).ToUnixTimeSeconds();
            }
            catch
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
        }

    }
}