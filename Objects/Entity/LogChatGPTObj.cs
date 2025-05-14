namespace NetworkMonitor.Objects.Entity {
    public class LogChatGPTObj{
        public LogChatGPTObj(){}
        private int _iD;
        private string _prompt="";
        private string _jsonData="";
        private string _jsonSentData="";
        private  bool _success;
        private string _message="";
        private DateTime _dateAdded=DateTime.UtcNow;


        public int ID { get=>_iD; set => _iD=value; }

        public string Prompt { get => _prompt; set => _prompt = value; }
        public string JsonData { get => _jsonData; set => _jsonData = value; }
        public bool Success { get => _success; set => _success = value; }
        public string JsonSentData { get => _jsonSentData; set => _jsonSentData = value; }
        public string Message { get => _message; set => _message = value; }
        public DateTime DateAdded { get => _dateAdded; set => _dateAdded = value; }
    }
}