using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects.ServiceMessage;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Security;
using System.Collections.Concurrent;
namespace NetworkMonitor.Objects.Repository
{
    public interface IRabbitListenerBase
    {

    }
    public abstract class RabbitListenerBase : IRabbitListenerBase
    {

        protected string _instanceName;
        //protected IChannel? _publishChannel;
        protected ILogger _logger;
        protected ConnectionFactory _factory;
        protected IConnection? _connection;
        protected List<RabbitMQObj> _rabbitMQObjs = new List<RabbitMQObj>();
        protected SystemUrl _systemUrl;
        protected bool _isTls = false;

        protected IRabbitListenerState _state;

        public RabbitListenerBase(ILogger logger, SystemUrl systemUrl, IRabbitListenerState? state = null, bool isTls = false)
        {
            _logger = logger;
            _systemUrl = systemUrl;
            _state = state ?? new RabbitListenerState();
            _isTls = isTls || systemUrl.UseTls;

        }
        protected abstract void InitRabbitMQObjs();

#pragma warning disable CS1998 // Disable warning about async method not containing await
        public async Task Shutdown()
        {
            try
            {

                foreach (var rabbitMQObj in _rabbitMQObjs)
                {
                    try
                    {
                        if (rabbitMQObj.ConnectChannel != null)
                        {
                            await rabbitMQObj.ConnectChannel.CloseAsync();
                            rabbitMQObj.ConnectChannel.Dispose();
                            rabbitMQObj.ConnectChannel = null;
                        }
                    }
                    catch { }
                }

                // Close and dispose of all channels


                // Close and dispose of the connection
                if (_connection != null)
                {
                    try
                    {
                        await _connection.CloseAsync();
                        _connection.Dispose();
                    }
                    catch (EndOfStreamException)
                    {
                    }
                    _connection = null;
                }

                _logger.LogInformation("RabbitMQ connection and channels closed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during RabbitMQ shutdown: {ex.Message}");
            }
        }
#pragma warning restore CS1998 // Restore warning
        // Inside RabbitListenerBase class
        public async Task<ResultObj> Setup()
        {
            _instanceName = _systemUrl.RabbitInstanceName;
            _factory = new ConnectionFactory
            {
                HostName = _systemUrl.RabbitHostName,
                UserName = _systemUrl.RabbitUserName,
                Password = _systemUrl.RabbitPassword,
                VirtualHost = _systemUrl.RabbitVHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                Port = _systemUrl.RabbitPort,
                RequestedHeartbeat = TimeSpan.FromSeconds(30),
                Ssl = new SslOption
                {
                    Enabled = _isTls,
                    ServerName = _systemUrl.RabbitHostName,
                    AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateNameMismatch |
                                 SslPolicyErrors.RemoteCertificateChainErrors
                    // If using a self-signed certificate, consider setting AcceptablePolicyErrors to SslPolicyErrors.RemoteCertificateChainErrors
                    // For production, use a valid certificate and handle the validation properly.
                }
            };
            _state.IsRabbitConnected = false;
            var result = new ResultObj();
            result.Message = " Rabbit Setup : ";
            result.Success = false;
            InitRabbitMQObjs();
            var (success, connection) = await RabbitConnectHelper.TryConnectAsync("RabbitListner", _factory, _logger);
            if (success)
            {
                _connection = connection;
            }
            else
            {
                result.Message += ($" Error : Rabbit Listener failed to establish connection to RabbitMQ server running at {_systemUrl.RabbitHostName}:{_systemUrl.RabbitPort}.");
                _logger.LogCritical(result.Message);
                _state.IsRabbitConnected = result.Success;
                _state.RabbitSetupMessage = result.Message;
                return result; // Exit if connection fails after max retries
            }

            if (_connection == null)
            {
                result.Message += "Failed to establish connection to RabbitMQ after maximum retries.";
                _logger.LogCritical(result.Message);
                _state.IsRabbitConnected = result.Success;
                _state.RabbitSetupMessage = result.Message;
                return result; // Exit if connection fails after max retries
            }
            // _connection.ConnectionShutdownAsync += OnConnectionShutdown;

            //_connection = _factory.CreateConnection();
            //_publishChannel = _connection.CreateModel();
            var channelTasks = _rabbitMQObjs.Select(async rabbitMQObj =>
            {
                rabbitMQObj.ConnectChannel = await _connection.CreateChannelAsync();
            });

            await Task.WhenAll(channelTasks);


            var results = new List<ResultObj>();
            results.Add(await DeclareQueues());
            results.Add(await DeclareConsumers());
            //results.Add(await BindChannelToConsumer());
            bool flag = true;
            string messages = "";
            results.ForEach(f => messages += f.Message);
            results.ForEach(f => flag = f.Success && flag);
            result.Success = flag;

            if (result.Success)
            {
                result.Message += $" Success : Connected to {_systemUrl.RabbitHostName}:{_systemUrl.RabbitPort} . Setup RabbitListener messages were : " + messages;
                _logger.LogInformation(result.Message);
            }
            else
            {
                result.Message += " Error : Failed to setup RabbitListener messages were : " + messages;
                _logger.LogCritical(result.Message);
            }
            _state.IsRabbitConnected = result.Success;
            _state.RabbitSetupMessage = result.Message;
            // _running=result.Success;
            return result;
        }
        private async Task OnConnectionShutdown(object? sender, ShutdownEventArgs e)
        {
            //_isRunning = false;
            _logger.LogWarning($" Warning : RabbitListner connection shutdown. Reason: {e.ReplyText}");
            try
            {
                await Setup();
            }
            catch (Exception ex)
            {
                _logger.LogError($" Error : RabbitListner unable to recreate connection . Error was : {ex.Message}. Attempting to recreate connection ...");

            }
        }

        protected async Task Reconnect()
        {

            // Close existing connection if open
            if (_connection != null)
            {
                try
                {
                    await _connection.CloseAsync();
                    _logger.LogWarning($" Warning : Reconnect event fired closing existing RabbitMQ Listner connection");
                }
                catch (Exception ex)
                {
                    _logger.LogError($" Error : closing existing RabbitMQ connection: {ex}");
                }
            }

            // Re-setup the connection and other components
            await Setup();
        }

        protected async Task<ResultObj> DeclareQueues()
        {
            var result = new ResultObj();
            result.Message = " RabbitListener DeclareQueues : ";
            var success = true;
            var declaredQueues = new ConcurrentBag<string>();
            var errors = new ConcurrentBag<string>();

            try
            {
                await Parallel.ForEachAsync(_rabbitMQObjs, async (rabbitMQObj, cancellationToken) =>
                {
                    try
                    {
                        var args = new Dictionary<string, object?>();
                        if (rabbitMQObj.MessageTimeout != 0)
                        {
                            args.Add("x-message-ttl", rabbitMQObj.MessageTimeout);
                        }

                        rabbitMQObj.QueueName = _instanceName + "-" + rabbitMQObj.ExchangeName;

                        if (rabbitMQObj.ConnectChannel == null)
                        {
                            errors.Add($"Error creating {rabbitMQObj.QueueName}: connection was null");
                            return;
                        }

                        // Sequential operations for this queue (must be in this order)
                        await rabbitMQObj.ConnectChannel.ExchangeDeclareAsync(
                            exchange: rabbitMQObj.ExchangeName,
                            type: rabbitMQObj.Type,
                            durable: true);

                        await rabbitMQObj.ConnectChannel.QueueDeclareAsync(
                            queue: rabbitMQObj.QueueName,
                            durable: true,
                            exclusive: false,
                            autoDelete: true,
                            arguments: args);

                        // Bind to each routing key
                        List<string> routingKeys = rabbitMQObj.RoutingKeys;
                        if (routingKeys == null || routingKeys.Count == 0 || rabbitMQObj.Type == "fanout")
                            routingKeys = new List<string> { "" }; // Default/fanout

                        foreach (var routingKey in routingKeys)
                        {
                            await rabbitMQObj.ConnectChannel.QueueBindAsync(
                                queue: rabbitMQObj.QueueName,
                                exchange: rabbitMQObj.ExchangeName,
                                routingKey: routingKey
                            );
                            if (routingKey != "") declaredQueues.Add(rabbitMQObj.QueueName + "-" + routingKey);
                            else declaredQueues.Add(rabbitMQObj.QueueName);
                        }


                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error processing {rabbitMQObj.ExchangeName}: {ex.Message}");
                    }
                });

                // Build result message
                foreach (var queue in declaredQueues)
                {
                    result.Message += $" {queue}";
                }

                foreach (var error in errors)
                {
                    result.Message += $" {error}";
                    success = false;
                }

                result.Message += success
                    ? " : Success : Declared all queues "
                    : " : Partial success : Some queues failed ";
                result.Success = success;
            }
            catch (Exception e)
            {
                result.Message += " Error : failed to declare queues. Error was : " + e.ToString() + " . ";
                _logger.LogError(result.Message);
                result.Success = false;
            }

            return result;
        }
        protected abstract Task<ResultObj> DeclareConsumers();
        protected async Task<ResultObj> BindChannelToConsumer()
        {
            var result = new ResultObj();
            result.Message = " RabbitRepo BindChannelToConsumer : ";
            try
            {

                foreach (var rabbitMQObj in _rabbitMQObjs)
                {
                    if (rabbitMQObj.ConnectChannel != null && rabbitMQObj.Consumer != null)
                        await rabbitMQObj.ConnectChannel.BasicConsumeAsync(queue: rabbitMQObj.QueueName,
                            autoAck: false,
                            consumer: rabbitMQObj.Consumer
                            );
                    else
                    {
                        throw new Exception(" RabbitMq Connect Channel is null can not Bind channel to Consumer");
                    }
                }
                result.Success = true;
                result.Message += " Success :  bound all consumers to queues ";
            }
            catch (Exception e)
            {
                string message = " Error : failed to bind all consumers to queues. Error was : " + e.ToString() + " . ";
                result.Message += message;
                _logger.LogError(result.Message);
                result.Success = false;
            }
            return result;
        }
        protected T? ConvertToObject<T>(object? sender, BasicDeliverEventArgs @event) where T : class
        {
            T? result = null;
            try
            {
                string json = Encoding.UTF8.GetString(@event.Body.ToArray());
                var cloudEvent = JsonSerializer.Deserialize(
                json, typeof(CloudEvent), SourceGenerationContext.Default)
                as CloudEvent;
                if (cloudEvent != null && cloudEvent.data != null && cloudEvent.data is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                {
                    result = JsonSerializer.Deserialize(
            jsonElement.GetRawText(), typeof(T), SourceGenerationContext.Default)
            as T;
                }
            }
            catch (Exception e)
            {
                _logger.LogError("Error: Unable to convert Object. Error was: " + e.ToString());
            }
            return result;
        }
        protected string? ConvertToString(object? sender, BasicDeliverEventArgs @event)
        {
            string? result = null;
            try
            {
                string json = Encoding.UTF8.GetString(@event.Body.ToArray());
                var cloudEvent = JsonSerializer.Deserialize(
                    json, typeof(CloudEvent), SourceGenerationContext.Default)
                    as CloudEvent;
                if (cloudEvent != null && cloudEvent.data != null) result = cloudEvent.data.ToString();
            }
            catch (Exception e)
            {
                _logger.LogError("Error: Unable to convert Object. Error was: " + e.ToString());
            }
            return result;
        }
        protected T? ConvertToList<T>(object? sender, BasicDeliverEventArgs @event) where T : class
        {
            T? result = null;
            try
            {
                string json = Encoding.UTF8.GetString(@event.Body.ToArray());
                var cloudEvent = JsonSerializer.Deserialize(
                 json, typeof(CloudEvent), SourceGenerationContext.Default)
                 as CloudEvent;
                if (cloudEvent != null && cloudEvent.data != null && cloudEvent.data is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
                {
                    result = JsonSerializer.Deserialize(
            jsonElement.GetRawText(), typeof(T), SourceGenerationContext.Default)
            as T;
                }
            }
            catch (Exception e)
            {
                _logger.LogError("Error: Unable to convert Object. Error was: " + e.ToString());
            }
            return result;
        }
    }
}