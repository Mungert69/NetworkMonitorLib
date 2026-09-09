

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class PaymentServiceInitObj : IBackendSignedMessage
    {
        public PaymentServiceInitObj() { }
        private bool _isPaymentServiceReady;

        public string BackendSignature { get; set; } = string.Empty;
        public bool IsPaymentServiceReady { get => _isPaymentServiceReady; set => _isPaymentServiceReady = value; }
    }
}
