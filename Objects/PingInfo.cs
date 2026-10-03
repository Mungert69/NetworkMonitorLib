using NetworkMonitor.Objects;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace NetworkMonitor.Objects
{
    public class PingInfo
    {
        public PingInfo()
        {
        }
        public PingInfo(PingInfo p)
        {
            ID = p.ID;
            DateSentInt = p.DateSentInt;
            Status = p.Status;
            RoundTripTime = p.RoundTripTime;
            MonitorPingInfoID = p.MonitorPingInfoID;
        }
        [Key]
        public ulong ID { get; set; }
        private uint _dateSentInt;
        /// <summary>
        /// The UTC time of the response event.
        /// </summary>
        [NotMapped]
        public DateTime DateSent
        {
            get
            {
                return DateTime.SpecifyKind(new DateTime(2022, 1, 1).AddSeconds(_dateSentInt), DateTimeKind.Utc);
            }
            set
            {
                DateTime dt1 = new DateTime(2022, 1, 1);
                TimeSpan ts;
                ts = value.Subtract(dt1.Date);
                _dateSentInt = (uint)ts.TotalSeconds;
            }
        }
        /// <summary>
        /// A fixed category label for this event, not a changing diagnostic message.
        /// </summary>
        /// <remarks>
        /// The backend interns each distinct status string in StatusList using a
        /// ushort ID. Keep the vocabulary bounded: never include measurements,
        /// addresses, timestamps, captured payloads or variable error details here.
        /// For example, use "BLE battery_voltage", not "BLE battery_voltage=13.67V".
        /// Put detailed readings and diagnostics in MonitorPingInfo messages, and
        /// retain the endpoint's existing numeric sample/scaling in RoundTripTime.
        /// This contract applies to every processor implementation, including ESP32.
        /// </remarks>
        [NotMapped]
        public string? Status { get; set; }
        /// <summary>
        /// References the shared StatusList lookup to reduce storage. Its ushort
        /// ID space is finite; Status must use fixed labels to avoid exhausting it.
        /// </summary>
        public ushort StatusID { get; set; }
        /// <summary>
        /// The response time of the event.
        /// </summary>
        public ushort? RoundTripTime { get; set; }

        [NotMapped]
        public int RoundTripTimeInt { get; set; }
        /// <summary>
        /// Parent MonitorPingInfo object ID.
        /// </summary>
        public int MonitorPingInfoID { get; set; }
        /// <summary>
        /// Datesent in a compressed integer format.
        /// </summary>
        public uint DateSentInt { get => _dateSentInt; set => _dateSentInt = value; }
        //public virtual MonitorPingInfo MonitorPingInfo{get;set;}
    }
}
