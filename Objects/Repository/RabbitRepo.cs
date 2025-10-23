using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Collections.Concurrent;
using NetworkMonitor.Utils;
using NetworkMonitor.Utils.Helpers;
using NetworkMonitor.Objects.Factory;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Connection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Security;
namespace NetworkMonitor.Objects.Repository
{
    public interface IRabbitRepo
    {
        SystemUrl SystemUrl { get; set; }
        Task Shutdown();
        string GetExchangeType(string exchangeName);
        Task PublishAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class;
        Task PublishAsync(string exchangeName, object? obj, string routingKey = "");
        Task<string> PublishJsonZAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class;
        Task<string> PublishJsonZWithIDAsync<T>(string exchangeName, T obj, string id, string routingKey = "") where T : class;
        Task<ResultObj> ConnectAndSetUp();
        Task<ResultObj> ShutdownRepo();
    }
    public class RabbitRepo : IRabbitRepo
    {
        private const int MaxRetries = -1; // Maximum number of retries
        private const int RetryDelayMilliseconds = 10000; // Time to wait between retries (60 seconds)

        //protected string _instanceName;
        protected IChannel? _publishChannel;
        protected ILogger _logger;
        protected ConnectionFactory _factory;
        protected IConnection? _connection;
        protected NetConnectConfig _netConfig;
        private SystemUrl _systemUrl;
        private bool _isRunning = false;
        private bool _isTls = false;
        private bool _isRestrictedPublishPerm = false;
        private int _maxRetries; // Maximum number of retries
        private int _retryDelayMilliseconds;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _exchangeLocks = new();
        private readonly object _isRunningLock = new();
        private bool _isReconnecting = false;
        private readonly object _reconnectLock = new();
        private readonly Dictionary<string, string> _exchangeTypes= new Dictionary<string, string>();


        public bool IsRunning
        {
            get
            {
                lock (_isRunningLock)
                {
                    return _isRunning;
                }
            }
            private set
            {
                lock (_isRunningLock)
                {
                    _isRunning = value;
                }
            }
        }

        private ConcurrentDictionary<string, bool> _exchangeCache = new ConcurrentDictionary<string, bool>();

        public SystemUrl SystemUrl { get => _systemUrl; set => _systemUrl = value; }
        private readonly SemaphoreSlim _publishSemaphore = new SemaphoreSlim(1, 1);


        public RabbitRepo(ILogger<RabbitRepo> logger, SystemParams systemParams)
        : this(logger, systemParams.ThisSystemUrl)
        {
            _exchangeTypes = systemParams.ExchangeTypes ?? new Dictionary<string, string>();

        }
#pragma warning disable CS8618
        public RabbitRepo(ILogger<RabbitRepo> logger, NetConnectConfig netConfig)
        {
            try
            {
                _logger = logger;
                _netConfig = netConfig;
                // Derive TLS from the effective SystemUrl only; root UseTls deprecated
                _isRestrictedPublishPerm = _netConfig.IsRestrictedPublishPerm;
                _systemUrl = _netConfig.LocalSystemUrl;
                _isTls = _systemUrl.UseTls;
                _logger?.LogInformation($" Use Tls in RabbitRepo NetConnectConfig ctor {_isTls}");
                
                _maxRetries = _netConfig.MaxRetries; // Maximum number of retries
                _retryDelayMilliseconds = _netConfig.RetryDelayMilliseconds;
                //ConnectAndSetUp();
                //_instanceName = _systemUrl.RabbitInstanceName;
                _netConfig.OnSystemUrlChangedAsync += HandleSystemUrlChangedAsync;
            }
            catch (Exception e)
            {
                _logger?.LogError($" Error : failed to initilise RabbitRepo . Error was : {e.Message}");
            }

        }

        public RabbitRepo(ILogger<RabbitRepo> logger, SystemUrl systemUrl)
        {
            try
            {
                _logger = logger;
                _systemUrl = systemUrl;
                _isTls = systemUrl.UseTls;
                _logger?.LogInformation($" Use Tls in RabbitRepo SystemUrl ctor {_isTls}");
                _maxRetries = MaxRetries; // Maximum number of retries
                _retryDelayMilliseconds = RetryDelayMilliseconds;
                // _instanceName = _systemUrl.RabbitInstanceName;
            }
            catch (Exception e)
            {
                _logger?.LogError($" Error : failed to initilise RabbitRepo . Error was : {e.Message}");
            }

        }
#pragma warning restore CS8618
        public string GetExchangeType(string exchangeName)
        {
            if (exchangeName.StartsWith("oa.", StringComparison.OrdinalIgnoreCase))
            {
                // Default to direct for oa.* exchanges
                return ExchangeType.Direct;
            }
            if (_exchangeTypes != null)
            {
                // Match the longest key that is a prefix of exchangeName
                var match = _exchangeTypes.Keys
                    .Where(key => exchangeName.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(k => k.Length) // Longest match wins
                    .FirstOrDefault();

                if (match != null)
                    return _exchangeTypes[match];

            }
            return ExchangeType.Fanout; // default fallback
        }


        private async Task HandleSystemUrlChangedAsync(SystemUrl newSystemUrl)
        {
            IsRunning = false;
            _systemUrl = newSystemUrl;
            await ConnectAndSetUp();
        }

        public async Task<ResultObj> ShutdownRepo()
        {
            var result = new ResultObj();

            try
            {
                if (_connection != null)
                {
                    _connection.ConnectionShutdownAsync -= OnConnectionShutdown;
                    await _connection.CloseAsync();
                    _connection.Dispose();
                    _connection = null;
                }
                result.Success = true;
            }
            catch (Exception e)
            {
                _logger.LogError($" Error : Could not close rabbit repo connection . Error was : {e.Message}");
            }


            return result;

        }
        public async Task<ResultObj> ConnectAndSetUp()
        {
            var result = new ResultObj();
            result.Message = " RabbitRepo : ConnectAndSetUp : ";
            IsRunning = false;
            try
            {
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
                        AcceptablePolicyErrors = SslPolicyErrors.None
                    }
                };
                var (success, connection) = await RabbitConnectHelper.TryConnectAsync("RabbitRepo", _factory, _logger, _maxRetries, _retryDelayMilliseconds);
                if (success)
                {
                    _connection = connection;
                }
                else
                {
                    result.Message += ($" Error : Rabbot Repo failed to establish connection to RabbitMQ server running at {_systemUrl.RabbitHostName}:{_systemUrl.RabbitPort} after {_maxRetries} retries.");
                    result.Success = false;
                    _logger.LogCritical(result.Message);
                    return result;
                }



                if (_connection != null)
                {
                    _connection.ConnectionShutdownAsync += OnConnectionShutdown;
                    _publishChannel = await _connection.CreateChannelAsync();
                    result.Success = true;
                    result.Message += $" Success : RabbitRepo Connected to RabbitMQ server {_systemUrl.RabbitHostName}:{_systemUrl.RabbitPort}";
                    _logger.LogInformation(result.Message);
                    IsRunning = true;

                    return result;
                }
                else
                {
                    result.Message += " Error : Connection object is null after trying to connect.";
                    result.Success = false;
                    _logger.LogCritical(result.Message);
                    return result;
                }

            }
            catch (Exception e)
            {
                result.Message += $" Error : could not setup RabbitRepo. Error was : {e.Message}";
                result.Success = false;
                _logger.LogCritical(result.Message);
                return result;
            }
            finally
            {

            }



        }



        private async Task OnConnectionShutdown(object? sender, ShutdownEventArgs e)
        {
            _logger.LogWarning($"Connection shutdown. Reason: {e.ReplyText}");
            IsRunning = false;

            lock (_reconnectLock)
            {
                if (_isReconnecting)
                {
                    _logger.LogInformation("Reconnection is already in progress. Skipping duplicate attempt.");
                    return;
                }
                _isReconnecting = true;
            }

            try
            {
                // Wait for automatic recovery to kick in
                await Task.Delay(RetryDelayMilliseconds);

                // If automatic recovery hasn't restored the connection, trigger manual reconnection
                if (_connection == null || !_connection.IsOpen)
                {
                    _logger.LogInformation("Automatic recovery failed. Attempting manual reconnection...");
                    await ConnectAndSetUp();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to reconnect. Reason: {ex.Message}");
            }
            finally
            {
                lock (_reconnectLock)
                {
                    _isReconnecting = false;
                }
            }
        }

        private async Task<bool> EnsureExchangeExistsAsync(string exchangeName)
        {
            bool state = false;
            if (_isRestrictedPublishPerm) return true;
            var exchangeLock = _exchangeLocks.GetOrAdd(exchangeName, _ => new SemaphoreSlim(1, 1));
            await exchangeLock.WaitAsync();
            try
            {
                string exchangeType = GetExchangeType(exchangeName);
                await _publishChannel!.ExchangeDeclareAsync(exchangeName, exchangeType, durable: true);
                state = true;
            }
            catch (Exception ex)
            {
                _exchangeCache.TryRemove(exchangeName, out _);

                _logger.LogError($" Error : could not create exchange {exchangeName}. Error was : {ex.Message}. ");
                state = false;
            }
            finally
            {
                exchangeLock.Release();
            }
            return state;
        }

        public async Task IsConnectionAndChannelOkAsync(string exchangeName)
        {

            // Create a random instance for all randomization
            Random rand = new Random();

            int randomOffsetMs = rand.Next(0, RetryDelayMilliseconds);

            int finalTimeoutMs = RetryDelayMilliseconds + randomOffsetMs;

            if (_isRestrictedPublishPerm) return;

            var startTime = DateTime.UtcNow;

            // Initial delay in milliseconds (e.g., 5 seconds)
            int timerDelayMs = 5000;

            while (!IsRunning || _connection == null || !_connection.IsOpen)
            {
                // Check elapsed time in ms
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > finalTimeoutMs)
                {
                    _logger.LogWarning("Automatic recovery is taking too long. Triggering manual reconnection...");
                    await ConnectAndSetUp();
                    // Introduce randomness into the multiplication factor
                    // For example, multiply the delay by a random factor between 1.5 and 3.0:
                    double randomMultiplier = 1.5 + (rand.NextDouble() * 1.5); // range: 1.5 to 3.0
                    timerDelayMs = (int)(timerDelayMs * randomMultiplier);
                }

                _logger.LogInformation(
                    $"Waiting for connection to be ready for {exchangeName}... " +
                    $"(Timeout: {finalTimeoutMs} ms, Current Delay: {timerDelayMs} ms)");

                await Task.Delay(timerDelayMs);
            }
            return;
        }


        private async Task<bool> IsExchangeOk(string exchangeName)
        {

            bool exchangeOk = false;
            // Check if exchange is already verified
            if (_exchangeCache.ContainsKey(exchangeName))
            {
                return true;
            }

            exchangeOk = await EnsureExchangeExistsAsync(exchangeName);
            if (exchangeOk)
            {
                _exchangeCache.TryAdd(exchangeName, true);
                return true;
            }
            else return false;

        }
#pragma warning disable CS1998
        public async Task PublishAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class
        {
            await _publishSemaphore.WaitAsync();
            try
            {
                await IsConnectionAndChannelOkAsync(exchangeName);
                await IsExchangeOk(exchangeName);
                string guid = Guid.NewGuid().ToString();
                string type = "NullOrEmpty";
                if (obj != null)
                {
                    type = obj.GetType().Name;
                }
                CloudEvent cloudEvent = new CloudEvent
                {
                    id = guid,
                    type = type,
                    source = SystemUrl.ExternalUrl,
                    time = DateTime.UtcNow,
                    data = obj
                };
                /*var formatter = new JsonEventFormatter();
                var json = formatter.ConvertToJObject(cloudEvent);
                string message = json.ToString();*/
                string message = JsonSerializer.Serialize(
                  cloudEvent, typeof(CloudEvent), SourceGenerationContext.Default);

                var body = Encoding.UTF8.GetBytes(message);
                if (_publishChannel != null)
                    await _publishChannel.BasicPublishAsync(exchange: exchangeName,
                                         routingKey: routingKey,
                                         body: body);
            }
            catch (RabbitMQ.Client.Exceptions.AlreadyClosedException ex)
            {
                _logger.LogError($"Publish failed. Channel or connection was closed. Reason: {ex.Message}");

                // Invalidate the cache for the exchange
                _exchangeCache.TryRemove(exchangeName, out _);


                throw;
            }
            catch
            {

                throw; // Rethrow the original exception
            }
            finally
            {
                _publishSemaphore.Release();
            }

        }
        public async Task PublishAsync(string exchangeName, object? obj, string routingKey = "")
        {
            await _publishSemaphore.WaitAsync();
            try
            {

                await IsConnectionAndChannelOkAsync(exchangeName);
                await IsExchangeOk(exchangeName);
                string guid = Guid.NewGuid().ToString();
                string type = "NullOrEmpty";
                if (obj != null)
                {
                    type = obj.GetType().Name;
                }
                CloudEvent cloudEvent = new CloudEvent
                {
                    id = guid,
                    type = type,
                    source = SystemUrl.ExternalUrl,
                    time = DateTime.UtcNow,
                    data = obj
                };
                string message = JsonSerializer.Serialize(
                  cloudEvent, typeof(CloudEvent), SourceGenerationContext.Default);

                var body = Encoding.UTF8.GetBytes(message);
                if (_publishChannel != null) await _publishChannel.BasicPublishAsync(exchange: exchangeName,
                                    routingKey: routingKey,
                                    body: body);

            }
            catch (RabbitMQ.Client.Exceptions.AlreadyClosedException ex)
            {
                _logger.LogError($"Publish failed. Channel or connection was closed. Reason: {ex.Message}");

                // Invalidate the cache for the exchange
                _exchangeCache.TryRemove(exchangeName, out _);


                throw;
            }
            catch
            {

                throw; // Rethrow the original exception
            }
            finally
            {
                _publishSemaphore.Release();
            }
        }
#pragma warning restore CS1998
        public async Task<string> PublishJsonZAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class
        {
            await _publishSemaphore.WaitAsync();
            string result = "";
            try
            {
                result = await PublishJsonZInternalAsync<T>(exchangeName, obj, routingKey);
            }
            catch (RabbitMQ.Client.Exceptions.AlreadyClosedException ex)
            {
                _logger.LogError($"Publish failed. Channel or connection was closed. Reason: {ex.Message}");

                // Invalidate the cache for the exchange
                _exchangeCache.TryRemove(exchangeName, out _);


                throw;
            }
            catch
            {

                throw; // Rethrow the original exception
            }
            finally
            {
                _publishSemaphore.Release();
            }
            return result;
        }

        public async Task<string> PublishJsonZInternalAsync<T>(string exchangeName, T obj, string routingKey = "") where T : class
        {

            await IsConnectionAndChannelOkAsync(exchangeName);
            await IsExchangeOk(exchangeName);
            string guid = Guid.NewGuid().ToString();
            var datajson = JsonUtils.WriteJsonObjectToString<T>(obj);
            string datajsonZ = StringCompressor.Compress(datajson);
            string type = "NullOrEmpty";
            if (obj != null)
            {
                type = obj.GetType().Name;
            }
            CloudEvent cloudEvent = new CloudEvent
            {
                id = guid,
                type = type,
                source = SystemUrl.ExternalUrl,
                time = DateTime.UtcNow,
                data = datajsonZ
            };
            /*var formatter = new JsonEventFormatter();
            var json = formatter.ConvertToJObject(cloudEvent);
            string message = json.ToString();*/
            string message = JsonSerializer.Serialize(
               cloudEvent, typeof(CloudEvent), SourceGenerationContext.Default);
            var body = Encoding.UTF8.GetBytes(message);
            if (_publishChannel != null) await _publishChannel.BasicPublishAsync(exchange: exchangeName,
                                routingKey: routingKey,
                                // body: formatter.EncodeBinaryModeEventData(cloudEvent));
                                body: body);
            return datajsonZ;
        }

        public async Task<string> PublishJsonZWithIDAsync<T>(string exchangeName, T obj, string id, string routingKey = "") where T : class
        {
            await _publishSemaphore.WaitAsync();
            string result = "";
            try
            {
                result = await PublishJsonZWithIDInternalAsync<T>(exchangeName, obj, id, routingKey);
            }
            catch (RabbitMQ.Client.Exceptions.AlreadyClosedException ex)
            {
                _logger.LogError($"Publish failed. Channel or connection was closed. Reason: {ex.Message}");

                // Invalidate the cache for the exchange
                _exchangeCache.TryRemove(exchangeName, out _);


                throw;
            }
            catch
            {

                throw; // Rethrow the original exception
            }
            finally
            {
                _publishSemaphore.Release();
            }
            return result;
        }

        public async Task<string> PublishJsonZWithIDInternalAsync<T>(string exchangeName, T obj, string id, string routingKey = "") where T : class
        {
            await IsConnectionAndChannelOkAsync(exchangeName);
            await IsExchangeOk(exchangeName);
            string guid = Guid.NewGuid().ToString();
            var datajson = JsonUtils.WriteJsonObjectToString<T>(obj);
            string datajsonZ = StringCompressor.Compress(datajson);
            string type = "NullOrEmpty";
            if (obj != null)
            {
                type = obj.GetType().Name;
            }
            Tuple<string, string> data = new Tuple<string, string>(datajsonZ, id);
            CloudEvent cloudEvent = new CloudEvent
            {
                id = guid,
                type = type,
                source = SystemUrl.ExternalUrl,
                time = DateTime.UtcNow,
                data = data
            };
            /*var formatter = new JsonEventFormatter();
            var json = formatter.ConvertToJObject(cloudEvent);
            string message = json.ToString();*/
            string message = JsonSerializer.Serialize(
               cloudEvent, typeof(CloudEvent), SourceGenerationContext.Default);
            var body = Encoding.UTF8.GetBytes(message);
            if (_publishChannel != null) await _publishChannel.BasicPublishAsync(exchange: exchangeName,
                                routingKey: routingKey,
                                body: body);
            return datajsonZ;
        }

        public async Task Shutdown()
        {
            try
            {
                if (_connection != null)
                {
                    _connection.ConnectionShutdownAsync -= OnConnectionShutdown;
                    await _connection.CloseAsync();
                    _connection.Dispose();
                }


                _logger.LogInformation("RabbitMQ connection and channel closed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during RabbitMQ shutdown: {ex.Message}");
            }
        }

    }
}
