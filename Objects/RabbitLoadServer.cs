namespace NetworkMonitor.Objects
{
    public class RabbitLoadServer
    {
        public RabbitLoadServer() { }

        public string Url { get; set; } = "";
        public string RabbitHostName { get; set; } = "";
        public ushort RabbitPort { get; set; } = 55671;

    }
   public class RabbitLoadServerResult
    {
        public string Message { get; set; } = "";
        public bool Success { get; set; } = false;
        public RabbitLoadServer? Data { get; set; } = new RabbitLoadServer();
    }
}
