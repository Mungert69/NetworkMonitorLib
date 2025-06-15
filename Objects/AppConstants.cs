namespace NetworkMonitor.Objects;

public static class AppConstants
{


#if DEV
        public static readonly string FrontendUrl = "https://devwww.readyforquantum.com";
        public static readonly string AppDomain = "readyforquantum.com";
        public static readonly string AppSecondLevelDomain = "readyforquantum";
        public static readonly string MailDomain="mahadeva.co.uk";
        public static readonly string readyforquantumGPTUrl="https://chatgpt.com/g/g-lpvGPvMq5-dev-free-network-monitor";
      public static readonly string ServiceServerName="devmonitorsrv";
#else
public static readonly string AppSecondLevelDomain = "readyforquantum";
   public static readonly string FrontendUrl = "https://readyforquantum.com";
   public static readonly string AppDomain = "readyforquantum.com";
   public static readonly string MailDomain = "mahadeva.co.uk";
   public static readonly string GPTUrl = "https://chatgpt.com/g/g-g0XMzU1nM-free-network-monitor";
   public static readonly string ServiceServerName = "monitorsrv";
#endif
}