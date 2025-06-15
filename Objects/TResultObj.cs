using System;

namespace NetworkMonitor.Objects
{

   public class TResultObj<TData> : TResultObj<TData, string>
{
    public TResultObj() : base() // Calls the parameterless constructor of the base class
    {
    }

    public TResultObj(string messageHeader) : base(messageHeader) // Calls the base class constructor that takes a string
    {
    }

    // If you have other constructors that need to pass parameters to the base class, you would define them here.
}

    public class TResultObj<TData, TSentData>  
    {

        public TResultObj()
        {

        }
        public TResultObj(string messageHeader)
        {
            _messageHeader = messageHeader;
            _message = messageHeader;
        }

        private string MessageWithoutHeader()
        {
            //if (_message==null) return "";
            // If MessageHeader exists and Message starts with it, remove it from the Message
            if (!string.IsNullOrEmpty(MessageHeader) && Message.StartsWith(MessageHeader) )
            {
                return _message.Substring(MessageHeader.Length).Trim();
            }
            return _message;
        }

        private void RemoveHeader()
        {
            _message = MessageWithoutHeader();
                _messageHeader = "";    
        }

    private void AddHeader(string header)
    {
        _messageHeader = header;
        _message = header + _message;
    }

    public void ReplaceHeader(string header)
    {
        RemoveHeader();
        AddHeader(header);
    }



    private string _message = "";

    private bool _success;

    private TData? _data;

    private TSentData? _sentData;

    private string? _messageHeader = null;

    private bool? _hasMorePages = false;
    private string? _dataFileUrl;
    /// <summary>
    /// Message containing important information about the status of this Api call. 
    /// </summary>
    public string Message { get => _message; set => _message = value; }
    /// <summary>
    /// Was the Api call successful.
    /// </summary>
    public bool Success { get => _success; set => _success = value; }
    /// <summary>
    /// The data return by the Api for consumption by the caller.
    /// </summary>
    public TData? Data { get => _data; set => _data = value; }
    /// <summary>
    /// Contains the data sent to the Api. It may contain extra fields added during processing by the Api. These can be used to help define the next call to the Api.
    /// </summary>
    public TSentData? SentData { get => _sentData; set => _sentData = value; }
    /// <summary>
    /// If there are more pages then query again. Repeat until this field return false or null;
    /// </summary>
    public bool? HasMorePages { get => _hasMorePages; set => _hasMorePages = value; }
    /// <summary>
    /// The url of a link to the Data returned with this result.
    /// </summary>
    public string? DataFileUrl { get => _dataFileUrl; set => _dataFileUrl = value; }
    public string? MessageHeader { get => _messageHeader; set => _messageHeader = value; }
}
}
