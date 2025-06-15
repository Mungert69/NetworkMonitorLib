
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace NetworkMonitor.Objects
{
    /// <summary>
    /// This class holds data that is used to edit hosts. 
    /// </summary>
    public class EditHost
    {
        public EditHost()
        {
        }

        private string _userID = "default";

        /// <summary>
        /// This is a string that is used to authenticate the Edit action for non authorised users. This key is returned when adding a host for the first time (IsEdit=false). It should be stored and sent with subsequent edit requests.
        /// Note authenticated users do not need this.
        /// </summary>
        public string? EditAuthKey { get; set; }
        /// <summary>
        /// If the user is default then AddUserEmail is used to identify different users. 
        /// If the user is authenticated this field is not necessary.
        /// </summary>
        public string? AddUserEmail { get; set; }

        /// <summary>
        /// The userId of the user editing the host.
        /// </summary>
        public string UserID { get => _userID; set => _userID = value; }

        /// <summary>
        /// This is the host ID used for identifying the host actions on the host.
        /// </summary>
        public int MonitorIPID { get; set; }
        /// <summary>
        /// On editting set the Enabled field.
        /// </summary>
        public bool? SetEnabled { get; set; }
        /// <summary>
        ///  On editting set the host address and hostname of the host.
        /// </summary>
        public string? SetAddress { get; set; }
        /// <summary>
        /// On Editting set the end point type. Example : http is a website ping, https is a ssl certificate check, httphtml is a website html load, icmp is a host ping, dns is a dns lookup, smtp is an email server helo message confirmation, quantum is a quantum safe encryption test, rawconnect is a low level raw socket connection.
        /// </summary>
        public string? SetEndPointType { get; set; }
        /// <summary>
        ///  On editting set the port of the service being monitored on edit. It should be zero if it is the standard port for the host end point type.
        /// </summary>
        public ushort? SetPort { get; set; }
        /// <summary>
        /// On editting set the time to wait for a timeout in milliseconds. If a timeout occurs the host is considered to be down or the test has failed.
        /// </summary>

        public int? SetTimeout { get; set; }

        /// <summary>
        /// On editting set if the host is hidden. Setting this to true effectively deletes the host from future monitoring. Historic data will still be available on the web interface.
        /// </summary>
        public bool? SetHidden { get; set; } = false;
        /// <summary>
        /// On editting set the authentication username for connection to the host.
        /// </summary>
        public string? SetUsername { get; set; }
        /// <summary>
        /// On editting set the authentication password for connection to the host.
        /// </summary>
        public string? SetPassword { get; set; }
        ///
        /// This field is required. The prompt that was entered by the user. The backend will use this to assist in creating a useful reponse for the user.
        ///
        public string? Prompt { get; set; }

        /// <summary>
        /// Set the Agent ID (AppID) .  For internet monitors this can be 1 for agent located at London - UK , 2 for Kansas - USA, 3 for Berlin - Germany. If it is a local agent then the AppID will be the same as UserInfo.UserID .
        /// </summary>
        public string? SetAppID { get; set; }
        /// <summary>
        /// Set The monitoring agents location for the host. The format is 'City - Country' for internet based agents. For local agents the format is 'emailaddress - agentid'.
        /// </summary>
        public string? SetAgentLocation { get; set; }

    }
}
