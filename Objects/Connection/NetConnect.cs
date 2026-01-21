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
        protected bool ExtendTimeout { get => _extendTimeout; set => _extendTimeout = value; }
        protected int ExtendTimeoutMultiplier { get => _extendTimeoutMultiplier; set => _extendTimeoutMultiplier = value; }

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
            _mpiConnect.SiteHash=_mpiStatic.SiteHash;
            int timeout = _mpiStatic.Timeout;
            if (ExtendTimeout)
            {
                timeout = _mpiStatic.Timeout * ExtendTimeoutMultiplier;
            }
            _cts.CancelAfter(TimeSpan.FromMilliseconds(timeout));

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
        }
        protected void ProcessStatus(string reply, ushort timeTaken, string extraData = "")
        {
             if (!string.IsNullOrEmpty(extraData)) _mpiConnect.Message = reply + " " + extraData;
           else _mpiConnect.Message = reply;
            _mpiConnect.PingInfo.Status = reply;
            _mpiConnect.PingInfo.RoundTripTime = timeTaken;
            _mpiConnect.IsUp = true;
        }

    }
}
