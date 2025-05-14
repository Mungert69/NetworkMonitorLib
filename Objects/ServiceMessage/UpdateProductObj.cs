using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ProductObj
    {
        private  string _priceId ="";
        private string _productName="";
        private int _hostLimit;
        private int _quantity;
        private string _description="";
        private bool _enabled;
        private int _price;
        private string _subscriptionUrl="";
        private string _subscribeInstructions="";

        public string PriceId { get => _priceId; set => _priceId = value; }
        public string ProductName { get => _productName; set => _productName = value; }
        public int HostLimit { get => _hostLimit; set => _hostLimit = value; }
         public int Quantity { get => _quantity; set => _quantity = value; }
        public string Description { get => _description; set => _description = value; }
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public int Price { get => _price; set => _price = value; }
        public string SubscriptionUrl { get => _subscriptionUrl; set => _subscriptionUrl = value; }
        public string SubscribeInstructions { get => _subscribeInstructions; set => _subscribeInstructions = value; }
    }

    public class UpdateProductObj{
        private string paymentServerUrl="";
        private List<ProductObj> _products = new List<ProductObj>();

        public List<ProductObj> Products { get => _products; set => _products = value; }
        public string PaymentServerUrl { get => paymentServerUrl; set => paymentServerUrl = value; }
    }
}