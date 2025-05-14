
  namespace NetworkMonitor.Objects.ServiceMessage
{
  public class CloudEvent
        {
          public CloudEvent(){}
            public string specversion { get; set; }="";
            public string id { get; set; }="";
            public string type { get; set; }="";
            public string source { get; set; }="";
            public DateTime time { get; set; }
            public string datacontenttype { get; set; }="";
            public object? data { get; set; }
        }
}