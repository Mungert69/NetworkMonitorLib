#define NetworkMonitorProcessor
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetworkMonitor.Utils;
namespace NetworkMonitor.Objects
{
    public class StatusObj
    {
        public const int MessageMaxLength = 4096;
        public StatusObj(){}
        public StatusObj(StatusObj copy){
                ID=copy.ID;
                DownCount=copy.DownCount;
                IsUp=copy.IsUp;
                AlertFlag=copy.AlertFlag;
                AlertSent=copy.AlertSent;
                EventTime=copy.EventTime;
                MonitorPingInfoID=copy.MonitorPingInfoID;
                Message=copy.Message;
        }
        [Key]
        public int ID { get; set; }
        private DateTime? _eventTime;
        private string? _message = "";
        
        // A thread safe DownCount
#if NetworkMonitorProcessor
    // Initialize the lock for microservice NetworkMontiorProcessor
    private int _downCount;
    
    // Set DownCount to 0
    public void ResetDownCount()
    {
        Interlocked.Exchange(ref _downCount, 0);
    }
    
    // Increment the DownCount field
    public void IncrementDownCount()
    {
        Interlocked.Increment(ref _downCount);
    }
    
    /// <summary>
    /// How many events in a row has the host been down.
    /// </summary>
    public int DownCount 
    {
        get 
        {
            return Interlocked.CompareExchange(ref _downCount, 0, 0);
        }
         set
        {
            Interlocked.Exchange(ref _downCount, value);
        }
    }
#else
    public int DownCount { get; set; }
#endif

/// <summary>
/// Did the host respond during the last reponse event for the data set.
/// </summary>
        public bool? IsUp { get; set; }
        /// <summary>
        /// Has an alert been raised for this host since the last alert reset.
        /// </summary>
        public bool AlertFlag { get; set; }
        /// <summary>
        /// Has an alert message been sent to user for this host since the last alert reset.
        /// </summary>
        public bool AlertSent { get; set; }
        /// <summary>
        /// The UTC time of the last reponse event for the data set. 
        /// </summary>
        public DateTime? EventTime
        {
            get
            {
                 if (_eventTime.HasValue)
                {
                    return DateTime.SpecifyKind(_eventTime.Value, DateTimeKind.Utc);
                }
                return null;
              
            }
            set { _eventTime = value; }
        }
        public int MonitorPingInfoID { get; set; }
#pragma warning disable IL2026
        /// <summary>
        /// The status message for the last reponse event for the data set.
        /// </summary>
        [MaxLength(MessageMaxLength)]
#pragma warning restore IL2026
        public string? Message
        {
            get { return _message; }
            set
            {
                if (value != null)
                    _message = StringUtils.Truncate(value, MessageMaxLength);
                else _message = "";
            }
        }
    }
}
