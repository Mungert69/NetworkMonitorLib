using RabbitMQ.Client.Events;

namespace NetworkMonitor.Objects.Repository.Helpers;

/// <summary>Opt-in MQTT ingress beside the existing AMQP processor exchanges.</summary>
public static class ProcessorMqttTopology
{
    public const string Exchange = "monitorProcessor.mqtt.v1";
    public const string Ready = "processor.out.ready";
    public const string Data = "processor.out.data";
    public const string StatusAlerts = "processor.out.status-alerts";
    public const string ResetAlerts = "processor.out.reset-alerts";
    public const string ScanRan = "processor.out.scan-ran";
    public const string ScanAck = "processor.out.scan-ack";

    public static bool IsIngress(BasicDeliverEventArgs message, string route) =>
        message.Exchange == Exchange && message.RoutingKey == route &&
        string.IsNullOrWhiteSpace(message.BasicProperties?.UserId);
}
