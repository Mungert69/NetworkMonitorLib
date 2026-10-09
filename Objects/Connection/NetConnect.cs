using NetworkMonitor.Objects;
using System;
using System.Text;
using System.Text.RegularExpressions;
using NetworkMonitor.Utils;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
namespace NetworkMonitor.Connection
{
    public interface INetConnect
    {
        EndpointMeasurementMetadata Measurement => MeasurementAnalysisTemplates.Complete(new());
        IReadOnlyCollection<EndpointMeasurementMetadata> MeasurementVariants => Array.Empty<EndpointMeasurementMetadata>();
        ushort RoundTrip { get; set; }
        //MonitorPingInfo MonitorPingInfo { get; set; }
        //PingParams PingParams { get; set; }
        //int Timeout { get; set; }
        uint PiID { get; set; }
        bool IsLongRunning { get; set; }
        //PingInfo PingInfo { get; set; }
        bool IsRunning { get; set; }
        bool IsQueued { get; set; }
        bool IsEnabled { get; set; }
        MPIConnect MpiConnect { get; set; }
        MPIStatic MpiStatic { get; set; }
        CancellationTokenSource Cts { get; set; }
        void ConfigureTimeout(int defaultTimeout, bool clampToDefault = false) =>
            NetConnect.ApplyTimeoutPolicy(MpiStatic, defaultTimeout, clampToDefault);
        Task Connect();
        void PostConnect();
        void PreConnect();
        //TimeSpan RunningTime();
    }
    public abstract class NetConnect : INetConnect
    {
        private MPIConnect _mpiConnect = new MPIConnect();
        private MPIStatic _mpiStatic = new MPIStatic();
        private uint _piID;
        private bool _isEnabled = true;
        private bool _isRunning = false;
        private bool _isQueued = false;
        private bool _extendTimeout = false;
        private int _extendTimeoutMultiplier = 10;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        //private DateTime _dateSent;
        private PingParams _pingParams = new PingParams();
        private ushort _roundTrip;
        private bool _isLongRunning = false;
        protected Stopwatch Timer = new Stopwatch();
        protected ILogger? Logger { get; private set; }
        protected NetConnectConfig? NetConfig { get; private set; }
        protected ICmdProcessorProvider? CmdProcessorProvider { get; private set; }
        protected IBrowserHost? BrowserHost { get; private set; }
        private EndpointMeasurementMetadata? _configuredMeasurement;
        // One complete definition is the sole measurement metadata contract.
        public virtual EndpointMeasurementMetadata Measurement => _configuredMeasurement ??
            MeasurementAnalysisTemplates.Complete(new());
        internal void ConfigureMeasurement(EndpointMeasurementMetadata measurement) => _configuredMeasurement = measurement;
        // Constant subtype definitions, selected by whole-token matching in host Args.
        public virtual IReadOnlyCollection<EndpointMeasurementMetadata> MeasurementVariants => Array.Empty<EndpointMeasurementMetadata>();
        /// <summary>Dynamic connects must override with a finite array of literal labels.
        /// Changing readings belong in monitor diagnostics, never PingInfo.Status.</summary>
        public virtual IReadOnlyCollection<string> StatusLabels => Array.Empty<string>();
        private DynamicConnectStatusPolicy? _dynamicStatusPolicy;
        internal void SetDynamicStatusPolicy(DynamicConnectStatusPolicy policy) => _dynamicStatusPolicy = policy;
        public ushort RoundTrip { get => _roundTrip; set => _roundTrip = value; }
        //public PingParams PingParams { get => _pingParams; set => _pingParams = value; }
        public uint PiID { get => _piID; set => _piID = value; }
        public bool IsLongRunning { get => _isLongRunning; set => _isLongRunning = value; }
        //public PingInfo PingInfo { get => _pingInfo; set => _pingInfo = value; }
        public bool IsRunning { get => _isRunning; set => _isRunning = value; }
        public bool IsQueued { get => _isQueued; set => _isQueued = value; }
        public CancellationTokenSource Cts { get => _cts; set => _cts = value; }
        public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }
        public MPIConnect MpiConnect { get => _mpiConnect; set => _mpiConnect = value; }
        public MPIStatic MpiStatic { get => _mpiStatic; set => _mpiStatic = value; }
        protected virtual bool UsesOperationTimeout => true;
        protected bool ExtendTimeout { get => _extendTimeout; set => _extendTimeout = value; }
        protected int ExtendTimeoutMultiplier { get => _extendTimeoutMultiplier; set => _extendTimeoutMultiplier = value; }

        /// <summary>Apply this connect's timeout policy after copying configuration.</summary>
        public virtual void ConfigureTimeout(int defaultTimeout, bool clampToDefault = false) =>
            ApplyTimeoutPolicy(MpiStatic, defaultTimeout, clampToDefault);

        internal static void ApplyTimeoutPolicy(MPIStatic configuration, int defaultTimeout, bool clampToDefault)
        {
            if (configuration.Timeout == 0 || (clampToDefault && configuration.Timeout > defaultTimeout))
                configuration.Timeout = defaultTimeout;
        }

        public abstract Task Connect();
        public virtual void Init(
            ILogger logger,
            NetConnectConfig cfg,
            ICmdProcessorProvider? cmdProcessorProvider = null,
            IBrowserHost? browserHost = null)
        {
            Logger = logger;
            NetConfig = cfg;
            CmdProcessorProvider = cmdProcessorProvider;
            BrowserHost = browserHost;
        }
        //public TimeSpan RunningTime()
        //{
        //   return DateTime.UtcNow.Subtract(_dateSent);
        //}
        public void PreConnect()
        {
            IsRunning = true;
            //_dateSent = DateTime.UtcNow;
            _mpiConnect = new MPIConnect();
            _mpiConnect.PingInfo = new PingInfo()
            {
                ID = PiID,
                MonitorPingInfoID = _mpiStatic.MonitorIPID,
                DateSent = DateTime.UtcNow
            };
            _cts = new CancellationTokenSource();
            _mpiConnect.SiteHash = _mpiStatic.SiteHash;
            if (UsesOperationTimeout)
            {
                int timeout = ExtendTimeout ? _mpiStatic.Timeout * ExtendTimeoutMultiplier : _mpiStatic.Timeout;
                _cts.CancelAfter(TimeSpan.FromMilliseconds(timeout));
            }

        }

        public void PostConnect()
        {
            IsRunning = false;
            Cts.Dispose();
        }
        protected void SetSiteHash(string hash)
        {
            _mpiConnect.SiteHash = hash;
            _mpiStatic.SiteHash = hash;
        }
        protected void ProcessException(string message, string shortMessage)
        {
            message = Regex.Replace(message, @"\(.*\)", "");
            message = StringUtils.Truncate(message, StatusObj.MessageMaxLength);
            _mpiConnect.Message = _mpiStatic.EndPointType.ToUpper() + ": Failed to connect: " + message;
            _mpiConnect.IsUp = false;
            _mpiConnect.PingInfo.Status = shortMessage;
            _mpiConnect.PingInfo.RoundTripTime = UInt16.MaxValue;
            _dynamicStatusPolicy?.Validate(_mpiConnect);
        }
        protected void ProcessStatus(string reply, ushort timeTaken, string extraData = "")
        {
            if (!string.IsNullOrEmpty(extraData)) _mpiConnect.Message = reply + " " + extraData;
            else _mpiConnect.Message = reply;
            _mpiConnect.PingInfo.Status = reply;
            _mpiConnect.PingInfo.RoundTripTime = timeTaken;
            _mpiConnect.IsUp = true;
            _dynamicStatusPolicy?.Validate(_mpiConnect);
        }

    }
}
