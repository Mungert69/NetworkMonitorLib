using System.Text;
using NetworkMonitor.Utils;

namespace NetworkMonitor.Objects;
 public interface IGenericEmailObj
    {
        public Guid ID { get; set; }
        string MainContent { get; set; }
        string EmailTitle { get; set; }
        string MainHeading { get; set; }
        string ButtonUrl { get; set; }
        string ButtonText { get; set; }
        string CurrentYear { get; set; }
        string HeaderImageUri { get; set; }
        string HeaderImageFile { get; set; }
        string HeaderImageUrl { get; set; }
        string HeaderImageAlt { get; set; }

        string ExtraMessage { get; set; }
        UserInfo UserInfo { get; set; }
    }
public class GenericEmailObj : IGenericEmailObj
{
    public Guid ID{get;set;}
    public string MainContent { get; set; } = "";
    public string EmailTitle { get; set; } = "";
    public string MainHeading { get; set; } = "";
    public string ButtonUrl { get; set; } = $"{AppConstants.FrontendUrl}/dashboard";
    public string ButtonText { get; set; } = "Manage My Monitored Hosts";
    public string CurrentYear { get; set; } = DateTime.Now.Year.ToString();
    public string HeaderImageUri { get; set; } = $"https://{AppConstants.ServiceServerName }.{AppConstants.AppDomain}/";
    public string HeaderImageFile { get; set; } = "logo.jpg";
    public string HeaderImageUrl { get; set; }=$"{AppConstants.FrontendUrl}/img/logo.jpg";
    public string HeaderImageAlt { get; set; } = "Quantum Network Monitor Logo";
    public string ExtraMessage { get; set; }
    public UserInfo UserInfo { get; set; } = new UserInfo();
    
}