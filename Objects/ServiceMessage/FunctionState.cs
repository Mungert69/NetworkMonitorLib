namespace NetworkMonitor.Objects.ServiceMessage;

public class FunctionState
{
    private bool _isFunctionCall;
    private bool _isFunctionCallResponse;
    private bool _isFunctionCallError;
    private bool _isFunctionCallStatus;
    private bool _isFunctionStillRunning = false;

    public void SetFunctionState(bool functionCall, bool functionCallResponse, bool functionCallError, bool functionCallStatus, bool functionStillRunning)
    {
        _isFunctionCall = functionCall;
        _isFunctionCallResponse = functionCallResponse;
        _isFunctionCallError = functionCallError;
        _isFunctionCallStatus = functionCallStatus;
        _isFunctionStillRunning = functionStillRunning;
    }



    public void SetAsCall()
    {
        _isFunctionCall = true;
        _isFunctionCallResponse = false;
        _isFunctionCallError = false;
        _isFunctionCallStatus = false;
        _isFunctionStillRunning = false;
    }

     public void SetAsCallError()
    {
        _isFunctionCall = true;
        _isFunctionCallResponse = false;
        _isFunctionCallError = true;
        _isFunctionCallStatus = false;
        _isFunctionStillRunning = false;
    }

     public void SetAsNotCall()
    {
        _isFunctionCall = false;
        _isFunctionCallResponse = false;
        _isFunctionCallError = false;
        _isFunctionCallStatus = false;
        _isFunctionStillRunning = false;
    }

     public void SetOnlyResponse()
    {
        _isFunctionCall = false;
        _isFunctionCallResponse = true;
    }

    public void SetAsResponseRunning()
    {
        _isFunctionCall = false;
        _isFunctionCallResponse = true;
        _isFunctionCallError = false;
        _isFunctionCallStatus = false;
        _isFunctionStillRunning=true;
    }

     public void SetAsResponseComplete()
    {
        _isFunctionCall = false;
        _isFunctionCallResponse = true;
        _isFunctionCallError = false;
        _isFunctionCallStatus = false;
        _isFunctionStillRunning = false;
    }
     public void SetAsResponseStatus()
    {
        _isFunctionCall = false;
        _isFunctionCallResponse = true;
        _isFunctionCallError = false;
        _isFunctionCallStatus = true;
        _isFunctionStillRunning = true;
    }

      public void SetAsResponseStatusOnly()
    {
        _isFunctionCall = false;
        _isFunctionCallResponse = false;
        _isFunctionCallError = false;
        _isFunctionCallStatus = true;
        _isFunctionStillRunning = true;
    }
      public void SetAsResponseError()
    {
        _isFunctionCall = false;
        _isFunctionCallResponse = true;
        _isFunctionCallError = true;
        _isFunctionCallStatus = false;
        _isFunctionStillRunning = false;
    }
      public void SetAsResponseErrorComplete()
    {
        _isFunctionCall = false;
        _isFunctionCallResponse = true;
        _isFunctionCallError = false;
        _isFunctionCallStatus = false;
        _isFunctionStillRunning = false;
    }


    public bool IsFunctionCall
    {
        get => _isFunctionCall;
        set => _isFunctionCall = value;
    }

    public bool IsFunctionCallResponse
    {
        get => _isFunctionCallResponse;
        set => _isFunctionCallResponse = value;
    }

    public bool IsFunctionCallError
    {
        get => _isFunctionCallError;
        set => _isFunctionCallError = value;
    }

    public bool IsFunctionCallStatus
    {
        get => _isFunctionCallStatus;
        set => _isFunctionCallStatus = value;
    }

    public bool IsFunctionStillRunning
    {
        get => _isFunctionStillRunning;
        set => _isFunctionStillRunning = value;
    }

    public string StatesString()
    {
        return $"isFunctionCall: {_isFunctionCall}, isFunctionCallResponse: {_isFunctionCallResponse}, isFunctionCallError: {_isFunctionCallError}, isFunctionCallStatus: {_isFunctionCallStatus}, isFunctionStillRunning: {_isFunctionStillRunning}";
    }
}
