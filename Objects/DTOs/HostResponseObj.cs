using NetworkMonitor.Objects;
namespace NetworkMonitor.DTOs
{
    public class HostResponseObj : MonitorPingInfo{
/// <summary>
/// A list of Response data formatted in a human readable form. Status as a string, The time of the event and the response time.
/// </summary>
        public List<PingInfoDTO> PingInfosDTO{get;set;}=new List<PingInfoDTO>();
        public float RoundTripTimeStandardDeviation {get;set;}
        public int SuccessfulPings{get;set;}
        public int FailedPings{get;set;}
    }
}