namespace NetworkMonitor.Objects{

    public class UrlObject
{
    public UrlObject(){}
    private string _url="";
    public string Url 
    { 
        get { return _url; } 
        set 
        { 
            if (!value.Contains("://"))
            {
                value = "https://" + value;
            }
            var uri = new Uri(value);
            _url = $"{uri.Scheme}://{uri.Host}";
            Port = (ushort)uri.Port;
        } 
    }

    public ushort Port { get; set; }
}

}