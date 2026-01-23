using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Utils;
using NetworkMonitor.Objects;
namespace NetworkMonitor.Connection
{
    public interface INetConnectCollection
    {
        INetConnect this[int index] { get; }
        INetConnectFilterStrategy FilterStrategy { get; set; }
        int WaitingTasksCounter { get; }
        int SemaphoreCount { get; }

        void SetNetConnectConfig(NetConnectConfig config);
        Task WaitAllTasks();
        void SetPingParams(PingParams pingParams);
        Task NetConnectFactory(List<MonitorPingInfo> monitorPingInfos, PingParams pingParams, bool isInit, bool isDisable, SemaphoreSlim lockObj);
        void UpdateOrAddIncPingParams(MonitorPingInfo monitorPingInfo);
        void DisableAll(int id);
        string RemoveAndAdd(MonitorPingInfo monitorPingInfo);
        void UpdateOrAdd(MonitorPingInfo monitorPingInfo);
        bool IsNetConnectRunning(int monitorIPID);
        void Add(MonitorPingInfo monitorPingInfo);
        INetConnect GetNetConnectInstance(MonitorPingInfo monitorPingInfo);
        IEnumerable<INetConnect> GetFilteredNetConnects();
        IEnumerable<INetConnect> GetNonLongRunningNetConnects();
        Task HandleLongRunningTask(INetConnect netConnect, MergeMonitorPingInfo mergeMonitorPingInfo);
        Task HandleShortRunningTask(INetConnect netConnect, MergeMonitorPingInfo mergeMonitorPingInfo);
        string LogInfo(List<INetConnect> filteredNetConnects);
        void ResetSiteHash(int monitorIPID);
    }

    public delegate void MergeMonitorPingInfo(MPIConnect mpiConnect, int monitorIPID);
    //public delegate ResultObj RemovePublishedPingInfosForID(int monitorIPID);
    //public delegate Task ZeroMonitorPingInfo(MonitorPingInfo monitorPingInfo);
    public class NetConnectCollection : INetConnectCollection
    {
        private List<INetConnect> _netConnects;
        public INetConnect this[int index]
        {
            get
            {
                return _netConnects[index];
            }

        }
        private INetConnectFilterStrategy _filterStrategy;
        private readonly ILogger _logger;
        private SemaphoreSlim _taskSemaphore; // Limit to 5 concurrent tasks
        private int _waitingTasksCounter = 0;
        private int _maxTaskQueueSize = 100;

        // private bool _isLocked = false;
        private PingParams _pingParams = new PingParams();
        private IConnectFactory _connectFactory;
        private readonly TimeSpan _maxRunningTime = TimeSpan.FromSeconds(120);
        // public List<INetConnect> NetConnects { get => _netConnects; set => _netConnects = value; }
        public INetConnectFilterStrategy FilterStrategy { get => _filterStrategy; set => _filterStrategy = value; }
        public int WaitingTasksCounter { get => _waitingTasksCounter; }
        public int SemaphoreCount { get => _taskSemaphore.CurrentCount; }
        //public bool IsLocked { get => _isLocked; set => _isLocked = value; }
#pragma warning disable CS8618
        public NetConnectCollection(ILogger logger, NetConnectConfig config, IConnectFactory connectFactory)
        {
            _logger = logger;
            _connectFactory = connectFactory;
            _netConnects = new List<INetConnect>();
            SetNetConnectConfig(config);
            var filterConfigMessages = config.FilterStrategies
                .Select(fs => fs.ToString());

            var filterConfigString = string.Join(Environment.NewLine, filterConfigMessages);

            // Log the filter configurations along with MaxTaskQueueSize
            _logger.LogInformation($"NETCONNECT Configuration:\n{filterConfigString}\nMaxTaskQueueSize = {_maxTaskQueueSize}");

        }
#pragma warning restore CS8618

        public void SetNetConnectConfig(NetConnectConfig config)
        {
            _maxTaskQueueSize = config.MaxTaskQueueSize;
            _filterStrategy = new ConfigurableEndpointFilterStrategy(config.FilterStrategies);
            _taskSemaphore = new SemaphoreSlim(_maxTaskQueueSize);
        }
        public IEnumerable<INetConnect> GetFilteredNetConnects()
        {
            if (_filterStrategy is IEndpointSettingStrategy endpointSettingStrategy)
            {
                endpointSettingStrategy.SetTotalEndpoints(_netConnects);
            }
            return _netConnects.Where(_filterStrategy.ShouldInclude)
                               .Where(w => w.IsEnabled && w.MpiStatic.Enabled == true);
        }
        public async Task WaitAllTasks()
        {
            _logger.LogDebug(" NETCONNECT : Starting WaitAllTasks ");
            // Check that all tasks have been cancelled*/
            bool isRunning = true;
            int waitingTasksCount;
            DateTime startTime = DateTime.UtcNow;
            while (isRunning)
            {
                isRunning = _netConnects.ToArray().Any(nc => nc.IsRunning);
                // Count running tasks
                waitingTasksCount = _netConnects.ToArray().Count(nc => nc.IsRunning);
                // Wait one second and log this message again
                _logger.LogDebug(" NETCONNECT : Tasks Waiting.. " + (DateTime.UtcNow - startTime).TotalSeconds + " seconds. IsRunning Count = " + waitingTasksCount + ". _tasksWaitingCounter = " + _waitingTasksCounter + " . ");
                await Task.Delay(1000);
            }
            _waitingTasksCounter = 0;
            _logger.LogDebug(" NETCONNECT : Finished WaitAllTasks ");
        }

        public void SetPingParams(PingParams pingParams)
        {
            _pingParams = pingParams;
        }
        public async Task NetConnectFactory(List<MonitorPingInfo> monitorPingInfos, PingParams pingParams, bool isInit, bool isDisable, SemaphoreSlim lockObj)
        {
            await lockObj.WaitAsync();
            try
            {
                _pingParams = pingParams;
                if (isInit)
                    _netConnects = new List<INetConnect>();
                if (isDisable)
                {
                    // set all _netConnects to dislabed.
                    foreach (INetConnect netConnect in _netConnects)
                    {
                        netConnect.IsEnabled = false;
                    }
                }
                foreach (MonitorPingInfo monitorPingInfo in monitorPingInfos)
                {
                    if (isDisable)
                        Add(monitorPingInfo);
                    else
                        UpdateOrAddIncPingParams(monitorPingInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(" NETCONNECT : Error NetConnectFactory Exception : " + ex.Message);
            }
            finally
            {
                lockObj.Release();
            }
        }
        public void UpdateOrAddIncPingParams(MonitorPingInfo monitorPingInfo)
        {
            var netConnect = _netConnects.FirstOrDefault(x => x.MpiStatic.MonitorIPID == monitorPingInfo.MonitorIPID);
            if (netConnect == null)
                Add(monitorPingInfo);
            else
            {
                _connectFactory.UpdateNetConnectionInfo(netConnect, monitorPingInfo, _pingParams);
            }
        }
        public void DisableAll(int id)
        {
            // Set enabled = false for all _netConnects where MonitorIPID = id
            foreach (INetConnect netConnect in _netConnects.Where(w => w.MpiStatic.MonitorIPID == id))
            {
                netConnect.IsEnabled = false;
            }
        }
        public string RemoveAndAdd(MonitorPingInfo monitorPingInfo)
        {
            string message = "";
            INetConnect? netConnect = _netConnects.FirstOrDefault(w => w.MpiStatic.MonitorIPID == monitorPingInfo.MonitorIPID && w.IsEnabled == true);
            if (netConnect != null)
            {
                netConnect.IsEnabled = false;
            }
            Add(monitorPingInfo);
            return message;
        }


        public void ResetSiteHash(int monitorIPID)
        {
            var netConnect = _netConnects.FirstOrDefault(x => x.MpiStatic.MonitorIPID == monitorIPID);
            if (netConnect != null)
                netConnect.MpiStatic.SiteHash = null;
            else
            {
                _logger.LogWarning($"Warning : enable to find NetConnect with MonitorIPID {monitorIPID}");
            }
        }
        public void UpdateOrAdd(MonitorPingInfo monitorPingInfo)
        {
            var netConnect = _netConnects.FirstOrDefault(x => x.MpiStatic.MonitorIPID == monitorPingInfo.MonitorIPID);
            if (netConnect == null)
                Add(monitorPingInfo);
            else
            {
                _connectFactory.UpdateNetConnectionInfo(netConnect, monitorPingInfo);
            }
        }
        public bool IsNetConnectRunning(int monitorIPID)
        {
            var testNetConnect = _netConnects.FirstOrDefault(w => w.MpiStatic.MonitorIPID == monitorIPID);
            // We are not going to process if the NetConnect is still running.
            if (testNetConnect != null && testNetConnect.IsRunning)
            {
                return true;
            }
            return false;
        }
        public void Add(MonitorPingInfo monitorPingInfo)
        {
            var newMon = new MonitorPingInfo(monitorPingInfo);
            var netConnect = _connectFactory.GetNetConnectObj(monitorPingInfo, _pingParams);
            _netConnects.Add(netConnect);
        }

        public INetConnect GetNetConnectInstance(MonitorPingInfo monitorPingInfo)
        {
            var newMon = new MonitorPingInfo(monitorPingInfo);
            var netConnect = _connectFactory.GetNetConnectObj(monitorPingInfo, _pingParams);
            return netConnect;
        }

        // Method to return all non long running netConnects
        public IEnumerable<INetConnect> GetNonLongRunningNetConnects()
        {
            return _netConnects.Where(w => w.IsLongRunning == false);
        }
        public async Task HandleLongRunningTask(INetConnect netConnect, MergeMonitorPingInfo mergeMonitorPingInfo)
        {
            if (netConnect.IsRunning)
            {
                _logger.LogWarning($" NETCONNECT : Warning: The long running{netConnect.MpiStatic.EndPointType} task for MonitorIPID {netConnect.MpiStatic.MonitorIPID} is already running.");
                return;
            }
            if (!netConnect.IsEnabled)
            {
                _logger.LogWarning($" NETCONNECT : Warning: The  MonitorIPID {netConnect.MpiStatic.MonitorIPID} is disabled at start of RunLongTask");
                return;
            }
            if (netConnect.IsQueued)
            {
                _logger.LogWarning($" NETCONNECT : Warning: Rejecting {netConnect.MpiStatic.EndPointType} long running task for MonitorIPID {netConnect.MpiStatic.MonitorIPID} is already in queue");
                return;
            }
            try
            {
                // Increment waiting tasks counter
                Interlocked.Increment(ref _waitingTasksCounter);
                netConnect.IsQueued = true;
                // Check if the waitingTaskCounter exceeds the threshold
                if (WaitingTasksCounter > _maxTaskQueueSize)
                {
                    _logger.LogError($" NETCONNECT : Error: The waitingTaskCounter has reached {WaitingTasksCounter}, which exceeds the limit of {_maxTaskQueueSize}.");
                    // You can handle this situation here or log additional information if needed
                }
                // Wait for a semaphore slot
                await _taskSemaphore.WaitAsync(netConnect.Cts.Token);
                //removePingInfos(netConnect.MpiStatic.MonitorIPID);
                /*if (netConnect.MonitorPingInfo.IsZero)
                {
                    zeroMonitorPingInfo(netConnect.MonitorPingInfo,false);
                }*/
                //netConnect.Cts.CancelAfter(TimeSpan.FromMilliseconds(netConnect.MpiStatic.Timeout));
                _logger.LogDebug($" NETCONNECT : Starting {netConnect.MpiStatic.EndPointType} task for MonitorIPID: {netConnect.MpiStatic.MonitorIPID} . ");
                // Decrement waiting tasks counter
                Interlocked.Decrement(ref _waitingTasksCounter);
                netConnect.IsQueued = false;
                var task = netConnect.Connect();
                try
                {
                    await task;
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == netConnect.Cts.Token)
                {
                    await Task.Delay(1000);
                    if (netConnect.IsRunning) _logger.LogWarning($" NETCONNECT : Warning: {netConnect.MpiStatic.EndPointType}  Task for MonitorIPID {netConnect.MpiStatic.MonitorIPID} was canceled but is still running.");
                    else _logger.LogWarning($"--> NETCONNECT : Warning: {netConnect.MpiStatic.EndPointType}  Task for MonitorIPID {netConnect.MpiStatic.MonitorIPID} was canceled.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($" NETCONNECT : Error: {netConnect.MpiStatic.EndPointType} Task for MonitorIPID {netConnect.MpiStatic.MonitorIPID} failed with exception {ex.Message}.");
                }
            }
            finally
            {
                if (netConnect.IsEnabled) mergeMonitorPingInfo(netConnect.MpiConnect, netConnect.MpiStatic.MonitorIPID);
                _logger.LogDebug($" NETCONNECT : Finished {netConnect.MpiStatic.EndPointType} task for MonitorIPID: {netConnect.MpiStatic.MonitorIPID} . ");
                _taskSemaphore.Release(); // Release the semaphore slot
            }
        }
        public async Task HandleShortRunningTask(INetConnect netConnect, MergeMonitorPingInfo mergeMonitorPingInfo)
        {
            if (netConnect.IsRunning)
            {
                _logger.LogWarning($" NETCONNECT : Warning: The short running {netConnect.MpiStatic.EndPointType} task for MonitorIPID {netConnect.MpiStatic.MonitorIPID} is already running.");
                return;
            }
            if (!netConnect.IsEnabled)
            {
                _logger.LogWarning($" NETCONNECT : Warning: The  MonitorIPID {netConnect.MpiStatic.MonitorIPID} is disabled at start of RunShortTask");
                return;
            }
            try
            {
                //removePingInfos(netConnect.MpiStatic.MonitorIPID);
                /*if (netConnect.MonitorPingInfo.IsZero)
                {
                    zeroMonitorPingInfo(netConnect.MonitorPingInfo,false);
                }*/
                //netConnect.Cts.CancelAfter(TimeSpan.FromMilliseconds(netConnect.MpiStatic.Timeout));
                _logger.LogDebug($" NETCONNECT : Starting {netConnect.MpiStatic.EndPointType}  task for MonitorIPID: {netConnect.MpiStatic.MonitorIPID} . ");
                var task = netConnect.Connect();
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    _logger.LogError($" NETCONNECT : Error: {netConnect.MpiStatic.EndPointType}  Task for MonitorIPID {netConnect.MpiStatic.MonitorIPID} failed with exception {ex.Message}.");
                }
            }
            finally
            {
                if (netConnect.IsEnabled) mergeMonitorPingInfo(netConnect.MpiConnect, netConnect.MpiStatic.MonitorIPID);
                _logger.LogDebug($" NETCONNECT : Finished {netConnect.MpiStatic.EndPointType}  task for MonitorIPID: {netConnect.MpiStatic.MonitorIPID} . ");
            }
        }
        public string LogInfo(List<INetConnect> filteredNetConnects)
        {
            var result = new StringBuilder();
            /*var longRunningNetConnects = filteredNetConnects.Where(w => w.IsRunning == true && w.RunningTime() > _maxRunningTime).ToList();
            if (longRunningNetConnects.Count() > 0)
            {
                result.Append($" NETCONNECT : Warning : There are {longRunningNetConnects.Count()} NetConnects that have exceeded the MaxRunningTime of {_maxRunningTime} ms. ");
                _logger.LogWarning($" NETCONNECT : Warning : There are {longRunningNetConnects.Count()} NetConnects that have exceeded the MaxRunningTime of {_maxRunningTime} ms. ");
                foreach (var longRunningNetConnect in longRunningNetConnects)
                {
                    result.Append(" NETCONNECT : Warning : NetConnect : " + JsonUtils.WriteJsonObjectToString(longRunningNetConnect) + " . ");
                    _logger.LogWarning(" NETCONNECT : Warning : NetConnect : " + JsonUtils.WriteJsonObjectToString(longRunningNetConnect) + " . ");
                }
            }*/
            _logger.LogInformation($" NETCONNECT : Semaphore tasks waiting : {_waitingTasksCounter} . Slots remaining {SemaphoreCount}. ");
            return result.ToString();
        }
    }
}
