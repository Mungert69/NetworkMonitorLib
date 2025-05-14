using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace NetworkMonitor.Objects
{
    /// <summary>
    /// This is a class that respresents the data for a host. In order for the data to be userful to a user Do not! anonymise any of the fields in the class.
    /// </summary>
    [Index(nameof(MonitorIPID), nameof(DataSetID), Name = "IDX_MonitorIPID_DataSetID")]

    public class MonitorPingInfo
    {
        // Empty constructor for EF
        public MonitorPingInfo()
        {

        }
        // Copy constructor
        public MonitorPingInfo(MonitorPingInfo copy)
        {
            ID = copy.ID;
            AppID = copy.AppID;
            DataSetID = copy.DataSetID;
            Status = copy.Status;
            //DestinationUnreachable = copy.DestinationUnreachable;
            MonitorIPID = copy.MonitorIPID;
            Timeout = copy.Timeout;
            //TimeOuts = copy.TimeOuts;
            Address = copy.Address;
            Port = copy.Port;
            UserID = copy.UserID;
            EndPointType = copy.EndPointType;
            PacketsRecieved = copy.PacketsRecieved;
            PacketsLost = copy.PacketsLost;
            PacketsLostPercentage = copy.PacketsLostPercentage;
            RoundTripTimeMaximum = copy.RoundTripTimeMaximum;
            RoundTripTimeAverage = copy.RoundTripTimeAverage;
            RoundTripTimeTotal = copy.RoundTripTimeTotal;
            RoundTripTimeMinimum = copy.RoundTripTimeMinimum;
            DateStarted = copy.DateStarted;
            DateEnded = copy.DateEnded;
            MonitorStatus = copy.MonitorStatus;
            PredictStatus = copy.PredictStatus;
            PacketsSent = copy.PacketsSent;
            Enabled = copy.Enabled;
            PingInfos = new List<PingInfo>();
            Username = copy.Username;
            Password = copy.Password;
            AddUserEmail = copy.AddUserEmail;
            IsEmailVerified = copy.IsEmailVerified;
            IsArchived = copy.IsArchived;
            AgentLocation = copy.AgentLocation;
        }

        public MonitorPingInfo(MonitorPingInfo copy, bool copyAll)
        {
            ID = copy.ID;
            AppID = copy.AppID;
            DataSetID = copy.DataSetID;
            Status = copy.Status;
            //DestinationUnreachable = copy.DestinationUnreachable;
            MonitorIPID = copy.MonitorIPID;
            Timeout = copy.Timeout;
            //TimeOuts = copy.TimeOuts;
            Address = copy.Address;
            Port = copy.Port;
            UserID = copy.UserID;
            EndPointType = copy.EndPointType;
            PacketsRecieved = copy.PacketsRecieved;
            PacketsLost = copy.PacketsLost;
            PacketsLostPercentage = copy.PacketsLostPercentage;
            RoundTripTimeMaximum = copy.RoundTripTimeMaximum;
            RoundTripTimeAverage = copy.RoundTripTimeAverage;
            RoundTripTimeTotal = copy.RoundTripTimeTotal;
            RoundTripTimeMinimum = copy.RoundTripTimeMinimum;
            DateStarted = copy.DateStarted;
            DateEnded = copy.DateEnded;
            MonitorStatus = copy.MonitorStatus;
            PredictStatus = copy.PredictStatus;
            PacketsSent = copy.PacketsSent;
            Enabled = copy.Enabled;
            PingInfos = copy.PingInfos;
            Username = copy.Username;
            Password = copy.Password;
            AddUserEmail = copy.AddUserEmail;
            IsEmailVerified = copy.IsEmailVerified;
            IsArchived = copy.IsArchived;
            AgentLocation = copy.AgentLocation;
        }

        public void CopyForPredict(MonitorPingInfo copy)
        {
            ID = copy.ID;
            AppID = copy.AppID;
            DataSetID = copy.DataSetID;
            Status = copy.Status;
            MonitorIPID = copy.MonitorIPID;
            Timeout = copy.Timeout;
            Address = copy.Address;
            Port = copy.Port;
            UserID = copy.UserID;
            EndPointType = copy.EndPointType;
            PacketsRecieved = copy.PacketsRecieved;
            PacketsLost = copy.PacketsLost;
            PacketsLostPercentage = copy.PacketsLostPercentage;
            RoundTripTimeMaximum = copy.RoundTripTimeMaximum;
            RoundTripTimeAverage = copy.RoundTripTimeAverage;
            RoundTripTimeTotal = copy.RoundTripTimeTotal;
            RoundTripTimeMinimum = copy.RoundTripTimeMinimum;
            DateStarted = copy.DateStarted;
            DateEnded = copy.DateEnded;
            PacketsSent = copy.PacketsSent;
            Enabled = copy.Enabled;
            Username = copy.Username;
            Password = copy.Password;
            AddUserEmail = copy.AddUserEmail;
            IsEmailVerified = copy.IsEmailVerified;
            IsArchived = copy.IsArchived;
            AgentLocation = copy.AgentLocation;

        }
        public void CopyMonitorPingInfo(MonitorPingInfo copy)
        {
            ID = copy.ID;
            AppID = copy.AppID;
            DataSetID = copy.DataSetID;
            Status = copy.Status;
            //DestinationUnreachable = copy.DestinationUnreachable;
            MonitorIPID = copy.MonitorIPID;
            Timeout = copy.Timeout;
            //TimeOuts = copy.TimeOuts;
            Address = copy.Address;
            Port = copy.Port;
            UserID = copy.UserID;
            EndPointType = copy.EndPointType;
            PacketsRecieved = copy.PacketsRecieved;
            PacketsLost = copy.PacketsLost;
            PacketsLostPercentage = copy.PacketsLostPercentage;
            RoundTripTimeMaximum = copy.RoundTripTimeMaximum;
            RoundTripTimeAverage = copy.RoundTripTimeAverage;
            RoundTripTimeTotal = copy.RoundTripTimeTotal;
            RoundTripTimeMinimum = copy.RoundTripTimeMinimum;
            DateStarted = copy.DateStarted;
            DateEnded = copy.DateEnded;
            MonitorStatus = copy.MonitorStatus;
            PredictStatus = copy.PredictStatus;
            PacketsSent = copy.PacketsSent;
            Enabled = copy.Enabled;
            PingInfos = new List<PingInfo>();
            Username = copy.Username;
            Password = copy.Password;
            AddUserEmail = copy.AddUserEmail;
            IsEmailVerified = copy.IsEmailVerified;
            IsArchived = copy.IsArchived;
        }
#pragma warning disable IL2026
        [Key]
        [DatabaseGeneratedAttribute(DatabaseGeneratedOption.Identity)]
        public int ID { get; set; }

        /// <summary>
        /// The ID used to identify this MonitorPingInfo object.
        /// </summary> 
        [NotMapped]
        public int MonitorPingInfoID { get => ID; }

        private DateTime _dateStarted = DateTime.UtcNow;
        private DateTime? _dateEnded = DateTime.UtcNow;
        private int _roundTripTimeMinimum = 9999;
        private StatusObj _monitorStatus = new StatusObj();
        private PredictStatus? _predictStatus;
        private List<PingInfo> _pingInfos = new List<PingInfo>();
        /// <summary>
        /// The Data set id that this host data is contained within.
        /// </summary>

        public int DataSetID { get; set; }
        /// <summary>
        /// The current or last status of the host. Taken from monitorStatus.Message.
        /// </summary>

        [MaxLength(255)]
        public string? Status { get => _monitorStatus.Message; set => _monitorStatus.Message = value; }
        //public int DestinationUnreachable { get; set; }

        public int MonitorIPID { get; set; }
        public int Timeout { get; set; }

        /// <summary>
        /// The host name. This is the same as the Address. 
        /// </summary>
        public string Host { get { return Address; } }
        /// <summary>
        /// host address.
        /// </summary>

        [MaxLength(512)]
        public string Address { get; set; } = "";

        /// <summary>
        /// the port used if not the default for this type of end point and host.
        /// </summary>
        public ushort Port { get; set; }

        [MaxLength(50)]
        public string? UserID { get; set; }
        /// <summary>
        /// Username for host authentication.
        /// </summary>
        [MaxLength(512)]
        public string? Username { get; set; }
        /// <summary>
        /// Password for host authentication.
        /// </summary>

        [MaxLength(512)]
        public string? Password { get; set; }
        /// <summary>
        /// The endpoint type. Endpoint types are: quantum is a quantum safe encryption test, http is a website ping, https is an SSL certificate check, httphtml is a website HTML load, icmp is a host ping, dns is a DNS lookup, smtp is an email server helo message confirmation, rawconnect is a low-level raw socket connection, nmap is a nmap service scan of the host, nmapvuln is a nmap vulnerability scan of the host and crawlsite performs a simulated user crawl of the site that generates site traffic using chrome browser.
        /// </summary>

        [MaxLength(10)]
        public string EndPointType { get; set; } = "";
        /// <summary>
        /// The total number of host up or event success for this host in this data set.
        /// </summary>
        public int PacketsRecieved { get; set; }
        /// <summary>
        /// The total number of host down or event failed for the host in this data set.
        /// </summary>
        public int PacketsLost { get; set; }
        /// <summary>
        /// The percentage of host down or event failed for the host in this data set.
        /// </summary>
        public float PacketsLostPercentage { get; set; }
        /// <summary>
        /// The maximum response time of the host in this data set.
        /// </summary>

        public int RoundTripTimeMaximum { get; set; }
        /// <summary>
        /// Average response time for this host in this data set.
        /// </summary>
        public float RoundTripTimeAverage { get; set; }
        /// <summary>
        /// Total of all the response times for all packets sent to this host in this data set.
        /// </summary>

        public int RoundTripTimeTotal { get; set; }

        /// <summary>
        /// Total number of packets sent in this data set for the host.
        /// </summary>
        public int PacketsSent { get; set; }

        public bool Enabled { get; set; }

        /// <summary>
        /// The Monitoring agent ID. For internet monitors this can be 1 for agent located at London - UK , 2 for Kansas - USA, 3 for Berlin - Germany. If it is a local agent then the AppID will be the UserInfo.UserID .
        /// </summary>
        [MaxLength(255)]
        public string? AppID { get; set; } = "";
        /// <summary>
        /// At which UTC time was this data set started.
        /// </summary>
        public DateTime DateStarted
        {
            get
            {
                return DateTime.SpecifyKind(_dateStarted, DateTimeKind.Utc); ;
            }
            set { _dateStarted = value; }
        }
        /// <summary>
        /// At which UTC time does this data set end.
        /// </summary>
        public DateTime? DateEnded
        {
            get
            {
                if (_dateEnded.HasValue)
                {
                    return DateTime.SpecifyKind(_dateEnded.Value, DateTimeKind.Utc);
                }
                return null;
            }
            set { _dateEnded = value; }
        }

        public int RoundTripTimeMinimum { get => _roundTripTimeMinimum; set => _roundTripTimeMinimum = value; }
        /// <summary>
        /// Current or last Status of the host. If the host is down then monitorStatus.Isup = false . If Alert if flagged monitorStatus.AlertFlag = true. If Alert has been sent monitorStatus.AlertSent = true.
        /// </summary>
        public StatusObj MonitorStatus { get => _monitorStatus; set => _monitorStatus = value; }

        public PredictStatus? PredictStatus { get => _predictStatus; set => _predictStatus = value; }
        /// <summary>
        /// A list of PingInfo data for this host. Contains response times and IsUp for each monitor event.
        /// </summary>
        public virtual List<PingInfo> PingInfos { get => _pingInfos; set => _pingInfos = value; }


        private int _isDirtyDownCount;
        [NotMapped]
        public bool IsDirtyDownCount
        {
            get { return Interlocked.CompareExchange(ref _isDirtyDownCount, 0, 0) != 0; }
            set { Interlocked.Exchange(ref _isDirtyDownCount, value ? 1 : 0); }
        }

        /// <summary>
        /// The email address to which to send alerts for this host.
        /// </summary>
        [MaxLength(255)]
        public string? AddUserEmail { get; set; } = null;

        /// <summary>
        /// Is the email address to set alerts to for this host verified.
        /// </summary>
        public bool IsEmailVerified { get; set; } = false;

        /// <summary>
        /// This message should be displayed to the user. It will contain addtional information about the host.
        /// </summary>
        [NotMapped]
        public string? MessageForUser { get; set; }
        /// <summary>
        /// Has the response data been archived for this MonitorPingInfo.
        /// </summary>
        public bool IsArchived { get; set; } = false;
        /// <summary>
        /// The location of the monitoring agent for this host.
        /// </summary>
        [NotMapped]
        public string? AgentLocation { get; set; }
#pragma warning restore IL2026
    }
}


