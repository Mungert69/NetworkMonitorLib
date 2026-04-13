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
using System.Threading;
using NetworkMonitor.Objects.Repository.Helpers;
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
        private readonly AsyncLocal<string?> _currentPublisherUserId = new AsyncLocal<string?>();
        private readonly object _setupTaskLock = new();
        private Task<ResultObj>? _setupTask;
        private readonly CancellationTokenSource _shutdownCts = new();
        private volatile bool _isShuttingDown = false;

        public RabbitListenerBase(ILogger logger, SystemUrl systemUrl, IRabbitListenerState? state = null)
        {
            _logger = logger;
            _systemUrl = systemUrl;
            _state = state ?? new RabbitListenerState();
            _isTls = systemUrl.UseTls;
             _logger?.LogInformation($" Use Tls in RabbitListenerBase ctor {_isTls}");
               

        }
        protected abstract void InitRabbitMQObjs();

#pragma warning disable CS1998 // Disable warning about async method not containing await
        public async Task Shutdown()
        {
            _isShuttingDown = true;
            _shutdownCts.Cancel();
            await CloseResourcesAsync();
        }

        private async Task CloseResourcesAsync()
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
                    catch
                    {
                        _logger.LogWarning($"RabbitMQ channel for exchange {rabbitMQObj.ExchangeName} already closed or could not be closed.");
                    }
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
                        _logger.LogWarning("RabbitMQ connection already closed.");
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
        public Task<ResultObj> Setup()
        {
            return Setup(CancellationToken.None);
        }

        public Task<ResultObj> Setup(CancellationToken cancellationToken)
        {
            lock (_setupTaskLock)
            {
                if (_setupTask != null && !_setupTask.IsCompleted)
                {
                    _logger.LogInformation(" RabbitListener setup is already running; reusing existing attempt.");
                    return _setupTask;
                }

                _setupTask = SetupWithCancellation(cancellationToken);
                return _setupTask;
            }
        }

        private async Task<ResultObj> SetupWithCancellation(CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            return await SetupCore(linkedCts.Token);
        }

        private async Task<ResultObj> SetupCore(CancellationToken cancellationToken)
        {
            if (_rabbitMQObjs.Count > 0 || _connection != null)
            {
                await CloseResourcesAsync();
                _rabbitMQObjs.Clear();
            }
            _instanceName = _systemUrl.RabbitInstanceName;
            cancellationToken.ThrowIfCancellationRequested();
            _factory = new ConnectionFactory
            {
                HostName = _systemUrl.RabbitHostName,
                UserName = _systemUrl.RabbitUserName,
                Password = _systemUrl.RabbitPassword,
                VirtualHost = _systemUrl.RabbitVHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                Port = _systemUrl.RabbitPort,
                RequestedHeartbeat = TimeSpan.FromSeconds(120),
                HandshakeContinuationTimeout = TimeSpan.FromSeconds(40),
   
                Ssl = BuildSslOption()
            };
            _state.IsRabbitConnected = false;
            var result = new ResultObj();
            result.Message = " Rabbit Setup : ";
            result.Success = false;
            InitRabbitMQObjs();
            try
            {
                var (success, connection) = await RabbitConnectHelper.TryConnectAsync("RabbitListner", _factory, _logger, cancellationToken: cancellationToken);
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
            }
            catch (OperationCanceledException)
            {
                result.Message += " Cancelled : RabbitListener setup was cancelled.";
                result.Success = false;
                _logger.LogInformation(result.Message);
                _state.IsRabbitConnected = result.Success;
                _state.RabbitSetupMessage = result.Message;
                return result;
            }
            // _connection.ConnectionShutdownAsync += OnConnectionShutdown;

            //_connection = _factory.CreateConnection();
            //_publishChannel = _connection.CreateModel();
            var channelTasks = _rabbitMQObjs.Select(async rabbitMQObj =>
            {
                try
                {
                    rabbitMQObj.ConnectChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to create channel for RabbitMQ object: {rabbitMQObj}");
                    throw; // Rethrow to be caught by Task.WhenAll below
                }
            });

            try
            {
                await Task.WhenAll(channelTasks);
            }
            catch (AggregateException aggEx)
            {
                foreach (var ex in aggEx.InnerExceptions)
                {
                    _logger.LogError(ex, "Exception occurred during channel creation.");
                }
                // Handle partial setup or cleanup if necessary
                throw; // Optionally rethrow or handle as needed
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred during channel creation.");
                // Handle partial setup or cleanup if necessary
                throw; // Optionally rethrow or handle as needed
            }

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

        private SslOption BuildSslOption()
        {
            var sslOption = new SslOption
            {
                Enabled = _isTls,
                ServerName = _systemUrl.RabbitHostName,
                AcceptablePolicyErrors = SslPolicyErrors.None
            };

            LegacyAndroidSslHelper.Configure(_systemUrl, sslOption, _logger);
            return sslOption;
        }
        private async Task OnConnectionShutdown(object? sender, ShutdownEventArgs e)
        {
            //_isRunning = false;
            _logger.LogWarning($" Warning : RabbitListner connection shutdown. Reason: {e.ReplyText}");
            if (_isShuttingDown || _shutdownCts.IsCancellationRequested)
            {
                _logger.LogInformation("RabbitListener shutdown is in progress; skipping reconnect.");
                return;
            }
            try
            {
                await Setup(_shutdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("RabbitListener reconnect was cancelled.");
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
            await Setup(_shutdownCts.Token);
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

                        // --- Queue Naming ---
                        string queueName;
                        List<string>? routingKeys = rabbitMQObj.RoutingKeys;
                        if (routingKeys == null || routingKeys.Count == 0)
                        {
                            // No routing keys: treat as fanout (single queue, no suffix)
                            routingKeys = new List<string> { "" };
                            queueName = $"{_instanceName}-{rabbitMQObj.ExchangeName}";
                        }
                        else
                        {
                            // Routing key(s): queue name includes routing key(s)
                            string routingKeyPart = string.Join("-", routingKeys.OrderBy(r => r));
                            queueName = $"{_instanceName}-{rabbitMQObj.ExchangeName}-{routingKeyPart}";
                        }
                        rabbitMQObj.QueueName = queueName;

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


                        foreach (var routingKey in routingKeys)
                        {
                            await rabbitMQObj.ConnectChannel.QueueBindAsync(
                                queue: rabbitMQObj.QueueName,
                                exchange: rabbitMQObj.ExchangeName,
                                routingKey: routingKey
                            );
                            if (routingKey != "") declaredQueues.Add(rabbitMQObj.QueueName + " route " + routingKey + " , ");
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
                if (!HasValidatedPublisherUserId(@event))
                {
                    return null;
                }
                _currentPublisherUserId.Value = @event.BasicProperties?.UserId;

                string json = Encoding.UTF8.GetString(@event.Body.ToArray());
                var cloudEvent = JsonSerializer.Deserialize(
                    json, typeof(CloudEvent), SourceGenerationContext.Default)
                    as CloudEvent;
                if (cloudEvent != null && cloudEvent.data != null)
                {
                    if (cloudEvent.data is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                    {
                        result = JsonSerializer.Deserialize(
                            jsonElement.GetRawText(), typeof(T), SourceGenerationContext.Default)
                            as T;
                    }
                    else
                    {
                        _logger.LogWarning("CloudEvent.data is not a JsonElement object. Actual type: {DataType}", cloudEvent.data.GetType().FullName);
                    }
                }
                else _logger.LogWarning("CloudEvent or CloudEvent.data is null.");

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
                if (!HasValidatedPublisherUserId(@event))
                {
                    return null;
                }
                _currentPublisherUserId.Value = @event.BasicProperties?.UserId;

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
                if (!HasValidatedPublisherUserId(@event))
                {
                    return null;
                }
                _currentPublisherUserId.Value = @event.BasicProperties?.UserId;

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

        private bool HasValidatedPublisherUserId(BasicDeliverEventArgs @event)
        {
            if (!_systemUrl.RequirePublisherUserId)
            {
                return true;
            }

            string? publisherUserId = @event.BasicProperties?.UserId;
            if (!string.IsNullOrWhiteSpace(publisherUserId))
            {
                return true;
            }

            string cloudEventType = string.Empty;
            string cloudEventId = string.Empty;
            string cloudEventSource = string.Empty;
            string appId = string.Empty;
            TryExtractCloudEventDiagnostics(@event, out cloudEventType, out cloudEventId, out cloudEventSource, out appId);

            _logger.LogWarning(
                "Rabbit listener rejected message without validated user-id. Exchange={Exchange}, RoutingKey={RoutingKey}, DeliveryTag={DeliveryTag}, CloudEventType={CloudEventType}, CloudEventId={CloudEventId}, CloudEventSource={CloudEventSource}, AppID={AppID}",
                @event.Exchange,
                @event.RoutingKey,
                @event.DeliveryTag,
                cloudEventType,
                cloudEventId,
                cloudEventSource,
                appId);
            return false;
        }

        private static void TryExtractCloudEventDiagnostics(
            BasicDeliverEventArgs @event,
            out string cloudEventType,
            out string cloudEventId,
            out string cloudEventSource,
            out string appId)
        {
            cloudEventType = string.Empty;
            cloudEventId = string.Empty;
            cloudEventSource = string.Empty;
            appId = string.Empty;

            try
            {
                string json = Encoding.UTF8.GetString(@event.Body.ToArray());
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
                {
                    cloudEventType = typeEl.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    cloudEventId = idEl.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("source", out var sourceEl) && sourceEl.ValueKind == JsonValueKind.String)
                {
                    cloudEventSource = sourceEl.GetString() ?? string.Empty;
                }

                if (!root.TryGetProperty("data", out var dataEl))
                {
                    return;
                }

                if (dataEl.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                if (dataEl.TryGetProperty("AppID", out var appIdEl) && appIdEl.ValueKind == JsonValueKind.String)
                {
                    appId = appIdEl.GetString() ?? string.Empty;
                    return;
                }

                if (dataEl.TryGetProperty("appID", out var appIdLowerEl) && appIdLowerEl.ValueKind == JsonValueKind.String)
                {
                    appId = appIdLowerEl.GetString() ?? string.Empty;
                    return;
                }

                if (dataEl.TryGetProperty("appId", out var appIdCamelEl) && appIdCamelEl.ValueKind == JsonValueKind.String)
                {
                    appId = appIdCamelEl.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // Diagnostics-only path: never throw from validation logging.
            }
        }

        protected string GetPublisherUserId(BasicDeliverEventArgs @event)
        {
            // ConvertToObject/ConvertToString/ConvertToList already enforce presence of UserId.
            return @event.BasicProperties?.UserId ?? string.Empty;
        }

        protected string CurrentPublisherUserId
        {
            get { return _currentPublisherUserId.Value ?? string.Empty; }
        }

        protected bool ValidatePublisherIdentityForApp(
            ResultObj result,
            string? appId,
            string context,
            bool allowUserSetupPublisher = false,
            bool allowDefaultPublisher = false)
        {
            if (!_systemUrl.RequirePublisherUserId)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(CurrentPublisherUserId))
            {
                result.Success = false;
                result.Message += $" Error : {context} publisher identity is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(appId))
            {
                result.Success = false;
                result.Message += $" Error : {context} AppID is missing.";
                return false;
            }

            bool isSharedPublisher =
                (allowUserSetupPublisher && string.Equals(CurrentPublisherUserId, "usersetup", StringComparison.OrdinalIgnoreCase)) ||
                (allowDefaultPublisher && string.Equals(CurrentPublisherUserId, "default", StringComparison.OrdinalIgnoreCase));

            if (!isSharedPublisher && !appId.StartsWith(CurrentPublisherUserId + "-", StringComparison.Ordinal))
            {
                result.Success = false;
                result.Message += $" Error : {context} AppID '{appId}' is not bound to publisher '{CurrentPublisherUserId}'.";
                return false;
            }

            return true;
        }

        protected bool ShouldValidateClaimedUserAgainstPublisher(bool isSystemProcessor)
        {
            if (!_systemUrl.RequirePublisherUserId)
            {
                return false;
            }

            return !isSystemProcessor;
        }
    }
}
