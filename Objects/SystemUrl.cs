using System.Net;
namespace NetworkMonitor.Objects
{
    public class SystemUrl 
    {
        public SystemUrl(){}
       public string IPAddress {get;set;}="";
       public string ExternalUrl {get;set;}="";
       public string RabbitHostName {get;set;}="";
       public string RabbitInstanceName {get;set;}="";
       public ushort RabbitPort {get;set;}=55671;
       public string RabbitUserName {get;set;}="";
       public string RabbitPassword {get;set;}="";
       public string RabbitVHost {get;set;}="";
       public int MaxLoad {get;set;}=1500;
       public int MaxRuntime{get;set;}=60;
        public bool UseTls { get; set; } = true;
        public string Country { get; set; } = "US";
        public string Region { get; set; } = "America";
    }
}
