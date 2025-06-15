

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class PaymentServiceInitObj
    {
        public PaymentServiceInitObj(){}
          private bool _isPaymentServiceReady;

        public bool IsPaymentServiceReady { get => _isPaymentServiceReady; set => _isPaymentServiceReady = value; }
    }
}