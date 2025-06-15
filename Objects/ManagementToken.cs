

namespace NetworkMonitor.Objects
{
    public class ManagementToken
    {
        public ManagementToken(){}
       public string? access_token;
       public int expires_in;
       public string? scope;
       public string? token_type;
       public string? Domain;

       public bool IsReady=false;
    }
}
