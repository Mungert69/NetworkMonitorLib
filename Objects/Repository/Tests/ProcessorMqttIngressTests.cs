using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NetworkMonitor.Objects.Repository.Helpers;
using NetworkMonitor.Objects.ServiceMessage;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace NetworkMonitor.Objects.Repository.Tests;

public class ProcessorMqttIngressTests
{
    private sealed class Listener : RabbitListenerBase
    {
        public Listener(bool enabled) : base(NullLogger.Instance, new SystemUrl {
            RequirePublisherUserId = true, EnableMqttProcessorIngress = enabled
        }) { }

        protected override void InitRabbitMQObjs() { }
        protected override Task<ResultObj> DeclareConsumers() =>
            Task.FromResult(new ResultObj { Success = true });

        public ProcessorInitObj? Parse(BasicDeliverEventArgs message) =>
            ConvertToObject<ProcessorInitObj>(null, message, ProcessorMqttTopology.Ready);
    }

    private static BasicDeliverEventArgs Message(string exchange, string route,
        string? publisherUserId = null)
    {
        var properties = new BasicProperties();
        if (publisherUserId != null) properties.UserId = publisherUserId;
        const string json = "{\"data\":{\"AppID\":\"agent-1\",\"AuthKey\":\"key-1\"}}";
        return new BasicDeliverEventArgs("", 1, false, exchange, route,
            properties, Encoding.UTF8.GetBytes(json), CancellationToken.None);
    }

    [Fact]
    public void OnlyOptedInExactMqttRouteAllowsMissingUserId()
    {
        Assert.NotNull(new Listener(true).Parse(Message(
            ProcessorMqttTopology.Exchange, ProcessorMqttTopology.Ready)));
        Assert.Null(new Listener(false).Parse(Message(
            ProcessorMqttTopology.Exchange, ProcessorMqttTopology.Ready)));
        Assert.Null(new Listener(true).Parse(Message(
            "processorReady", ProcessorMqttTopology.Ready)));
        Assert.Null(new Listener(true).Parse(Message(
            ProcessorMqttTopology.Exchange, ProcessorMqttTopology.Data)));
    }

    [Fact]
    public void ExistingAmqpIdentityStillAccepted()
    {
        Assert.NotNull(new Listener(false).Parse(Message(
            "processorReady", "", "systemprocessor")));
    }
}
