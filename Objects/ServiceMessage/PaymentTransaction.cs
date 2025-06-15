

namespace NetworkMonitor.Objects.ServiceMessage{
    public class PaymentTransaction{
        public PaymentTransaction(){}
        private bool _isUpdate=false;
        private bool _isDelete=false;
        private bool _isCreate=false;
        private bool _isPayment=false;
        private int _id;
        private DateTime _eventDate;
        private UserInfo _userInfo=new UserInfo();
        private string _externalUrl="";
        private bool _pingInfosComplete;

        private bool _isComplete=false;
        private DateTime _completedDate;

        private TResultObj<string> _result=new TResultObj<string>();

        private int _retryCount=0;
        private string _eventId="";
        private string _priceId="";

        public bool IsUpdate { get => _isUpdate; set => _isUpdate = value; }
        public int Id { get => _id; set => _id = value; }
        public DateTime EventDate { get => _eventDate; set => _eventDate = value; }
        public UserInfo UserInfo { get => _userInfo; set => _userInfo = value; }
        public bool IsComplete { get => _isComplete; set => _isComplete = value; }
        public TResultObj<string> Result { get => _result; set => _result = value; }
        public DateTime CompletedDate { get => _completedDate; set => _completedDate = value; }
        public int RetryCount { get => _retryCount; set => _retryCount = value; }
        public string ExternalUrl { get => _externalUrl; set => _externalUrl = value; }
        public bool PingInfosComplete { get => _pingInfosComplete; set => _pingInfosComplete = value; }
        public string EventId { get => _eventId; set => _eventId = value; }
        public bool IsDelete { get => _isDelete; set => _isDelete = value; }
        public bool IsCreate { get => _isCreate; set => _isCreate = value; }
        public string PriceId { get => _priceId; set => _priceId = value; }
        public bool IsPayment { get => _isPayment; set => _isPayment = value; }
    }
}