

namespace NetworkMonitor.Objects
{
    public class DelHost : UserInfo
    {
        public DelHost()
        {
        }
        public DelHost(UserInfo userInfo){
        this.UserID = userInfo.UserID;
        this.DateCreated = userInfo.DateCreated;
        this.HostLimit = userInfo.HostLimit;
        this.DisableEmail = userInfo.DisableEmail;
        this.Status = userInfo.Status;
        this.Name = userInfo.Name;
        this.Given_name = userInfo.Given_name;
        this.Family_name = userInfo.Family_name;
        this.Nickname = userInfo.Nickname;
        this.Sub = userInfo.Sub;
        this.Enabled = userInfo.Enabled;
        this.AccountType = userInfo.AccountType;
        this.Email = userInfo.Email;
        this.Email_verified = userInfo.Email_verified;
        this.Picture = userInfo.Picture;
        this.Updated_at = userInfo.Updated_at;
        this.LastLoginDate = userInfo.LastLoginDate;
        this.CustomerId = userInfo.CustomerId;
        this.CancelAt = userInfo.CancelAt;
        // Dont copy the MonitorIPs
        this.MonitorIPs = new List<MonitorIP>() ;

        }
       public int Index{get;set;}
    }
}
