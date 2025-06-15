using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects.ServiceMessage
{
    public class ChatServiceInitObj
    {
        public ChatServiceInitObj(){}
        private bool _isServiceReady;

        public bool IsServiceReady { get => _isServiceReady; set => _isServiceReady = value; }
    }
}