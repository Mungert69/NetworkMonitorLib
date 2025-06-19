using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NetworkMonitor.Objects.Repository
{

    public class RabbitMQObj
    {
        private IChannel? _connectChannel;
        private AsyncEventingBasicConsumer? _consumer;
        private string _exchangeName = "";
        private string funcName = "";

        private string _queueName = "";
        private int _messageTimeout = 0;


        public IChannel? ConnectChannel { get => _connectChannel; set => _connectChannel = value; }
        public AsyncEventingBasicConsumer? Consumer { get => _consumer; set => _consumer = value; }
        public string ExchangeName { get => _exchangeName; set => _exchangeName = value; }
        public string QueueName { get => _queueName; set => _queueName = value; }
        public string FuncName { get => funcName; set => funcName = value; }
        public int MessageTimeout { get => _messageTimeout; set => _messageTimeout = value; }
        public List<string> RoutingKeys { get; set; } = new List<string>();

        /// <summary>
        /// The type of the exchange: "fanout", "direct", "topic", etc.
        /// </summary>
        public string Type { get; set; } = ExchangeType.Fanout;
    }
}