namespace NetworkMonitor.DTOs
{
    public class PingInfoDTO
    {
        /// <summary>
        /// The datetime of the monitor event.
        /// </summary>
        public DateTime DateSent { get; set; }

        /// <summary>
        /// The status of this event.
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// The response time of the event.
        /// </summary>
        public int ResponseTime { get; set; }
    }
}
