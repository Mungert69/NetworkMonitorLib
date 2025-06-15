using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class RegisteredUser
    {
        public RegisteredUser(){}
        private string _userId="";
        private string _customerId="";
        private string _externalUrl="";
        private string _userEmail="";
        public string UserId { get => _userId; set => _userId = value; }
        public string CustomerId { get => _customerId; set => _customerId = value; }
        public string ExternalUrl { get => _externalUrl; set => _externalUrl = value; }
        public string UserEmail { get => _userEmail; set => _userEmail = value; }
    }
}