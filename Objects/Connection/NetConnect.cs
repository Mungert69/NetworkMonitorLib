using NetworkMonitor.Objects;
using System;
using System.Text;
using System.Text.RegularExpressions;
using NetworkMonitor.Utils;
using System.Threading.Tasks;
using System.Diagnostics;
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
        private CancellationTokenSource _cts = new CancellationTokenSource();
        //private DateTime _dateSent;
        private PingParams _pingParams = new PingParams();
        private ushort _roundTrip;
        private bool _isLongRunning = false;
        protected Stopwatch Timer = new Stopwatch();
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
        public abstract Task Connect();
        //public TimeSpan RunningTime()
        //{
        //   return DateTime.UtcNow.Subtract(_dateSent);
        //}
        public void PreConnect()
        {
            IsRunning = true;
            //_dateSent = DateTime.UtcNow;
            MpiConnect = new MPIConnect();
            _mpiConnect.PingInfo = new PingInfo()
            {
                ID = PiID,
                MonitorPingInfoID = _mpiStatic.MonitorIPID,
                DateSent = DateTime.UtcNow
            };
            _cts = new CancellationTokenSource();
            _cts.CancelAfter(TimeSpan.FromMilliseconds(_mpiStatic.Timeout));

        }
        public void ExtendCancelAfterTimeout()
        {  // Cancel the previous CancellationTokenSource if it's still active
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel(); // Cancels the ongoing operation if applicable
            _cts.Dispose(); // Dispose the old CTS
        }

        // Create a new CancellationTokenSource with the new delay
        _cts = new CancellationTokenSource();
       
            _cts.CancelAfter(TimeSpan.FromMilliseconds(_mpiStatic.Timeout * 10));
        }
        public void PostConnect()
        {
            IsRunning = false;
            Cts.Dispose();
        }
        protected void ProcessException(string message, string shortMessage)
        {
            message = Regex.Replace(message, @"\(.*\)", "");
            message = StringUtils.Truncate(message, 255);
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
