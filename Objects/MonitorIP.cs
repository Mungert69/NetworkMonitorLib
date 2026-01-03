using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using NetworkMonitor.Utils;
using System.Text.Json.Serialization;


namespace NetworkMonitor.Objects
{

    /// <summary>
    /// This class contains information about a host being monitored. Show the actual data do not anonymise this data. Sensitive fields have been filtered.
    /// </summary>
    public class MonitorIP
    {
#pragma warning disable IL2026

        public MonitorIP(MonitorIP newMonIP)
        {
            // Copy each property from 'newMonIP' to 'this'.
            this.ID = newMonIP.ID;
            this.Enabled = newMonIP.Enabled;
            this.Address = newMonIP.Address;
            this.EndPointType = newMonIP.EndPointType;
            this.Port = newMonIP.Port;
            this.Timeout = newMonIP.Timeout;
            this.UserInfoUserID = newMonIP.UserInfoUserID;
            this.UserID = newMonIP.UserID;
            this.Hidden = newMonIP.Hidden;
            this.AppID = newMonIP.AppID;
            this.Username = newMonIP.Username;
            this.Password = newMonIP.Password;
            this.Args = newMonIP.Args;
            this.UserInfo = newMonIP.UserInfo; // Note: This will copy the reference, not the object itself.
            this.AddUserEmail = newMonIP.AddUserEmail;
            this.IsEmailVerified = newMonIP.IsEmailVerified;
            this.EditAuthKey = newMonIP.EditAuthKey;
            this.MessageForUser = newMonIP.MessageForUser;
            this.DateAdded = newMonIP.DateAdded;
            this.SiteHash= newMonIP.SiteHash;
            this.MonitorModelConfigId = newMonIP.MonitorModelConfigId;
            if (newMonIP.ModelConfig != null)
            {
                this.ModelConfig = new MonitorModelConfig
                {
                    ChangeConfidence = newMonIP.ModelConfig.ChangeConfidence,
                    SpikeConfidence = newMonIP.ModelConfig.SpikeConfidence,
                    ChangePreTrain = newMonIP.ModelConfig.ChangePreTrain,
                    SpikePreTrain = newMonIP.ModelConfig.SpikePreTrain,
                    PredictWindow = newMonIP.ModelConfig.PredictWindow,
                    SpikeDetectionThreshold = newMonIP.ModelConfig.SpikeDetectionThreshold,
                    RunLength = newMonIP.ModelConfig.RunLength,
                    KOfNK = newMonIP.ModelConfig.KOfNK,
                    KOfNN = newMonIP.ModelConfig.KOfNN,
                    MadAlpha = newMonIP.ModelConfig.MadAlpha,
                    MinBandAbs = newMonIP.ModelConfig.MinBandAbs,
                    MinBandRel = newMonIP.ModelConfig.MinBandRel,
                    RollSigmaWindow = newMonIP.ModelConfig.RollSigmaWindow,
                    BaselineWindow = newMonIP.ModelConfig.BaselineWindow,
                    SigmaCooldown = newMonIP.ModelConfig.SigmaCooldown,
                    MinRelShift = newMonIP.ModelConfig.MinRelShift,
                    SampleRows = newMonIP.ModelConfig.SampleRows,
                    NearMissFraction = newMonIP.ModelConfig.NearMissFraction,
                    LogJson = newMonIP.ModelConfig.LogJson,
                    ChangeRunLength = newMonIP.ModelConfig.ChangeRunLength,
                    ChangeKOfNK = newMonIP.ModelConfig.ChangeKOfNK,
                    ChangeKOfNN = newMonIP.ModelConfig.ChangeKOfNN,
                    ChangeMadAlpha = newMonIP.ModelConfig.ChangeMadAlpha,
                    ChangeMinBandAbs = newMonIP.ModelConfig.ChangeMinBandAbs,
                    ChangeMinBandRel = newMonIP.ModelConfig.ChangeMinBandRel,
                    ChangeRollSigmaWindow = newMonIP.ModelConfig.ChangeRollSigmaWindow,
                    ChangeBaselineWindow = newMonIP.ModelConfig.ChangeBaselineWindow,
                    ChangeSigmaCooldown = newMonIP.ModelConfig.ChangeSigmaCooldown,
                    ChangeMinRelShift = newMonIP.ModelConfig.ChangeMinRelShift,
                    ChangeSampleRows = newMonIP.ModelConfig.ChangeSampleRows,
                    ChangeNearMissFraction = newMonIP.ModelConfig.ChangeNearMissFraction,
                    ChangeLogJson = newMonIP.ModelConfig.ChangeLogJson,
                    SpikeRunLength = newMonIP.ModelConfig.SpikeRunLength,
                    SpikeKOfNK = newMonIP.ModelConfig.SpikeKOfNK,
                    SpikeKOfNN = newMonIP.ModelConfig.SpikeKOfNN,
                    SpikeMadAlpha = newMonIP.ModelConfig.SpikeMadAlpha,
                    SpikeMinBandAbs = newMonIP.ModelConfig.SpikeMinBandAbs,
                    SpikeMinBandRel = newMonIP.ModelConfig.SpikeMinBandRel,
                    SpikeRollSigmaWindow = newMonIP.ModelConfig.SpikeRollSigmaWindow,
                    SpikeBaselineWindow = newMonIP.ModelConfig.SpikeBaselineWindow,
                    SpikeSigmaCooldown = newMonIP.ModelConfig.SpikeSigmaCooldown,
                    SpikeMinRelShift = newMonIP.ModelConfig.SpikeMinRelShift,
                    SpikeSampleRows = newMonIP.ModelConfig.SpikeSampleRows,
                    SpikeNearMissFraction = newMonIP.ModelConfig.SpikeNearMissFraction,
                    SpikeLogJson = newMonIP.ModelConfig.SpikeLogJson,
                    UpdatedUtc = newMonIP.ModelConfig.UpdatedUtc,
                    UpdatedBy = newMonIP.ModelConfig.UpdatedBy,
                    Notes = newMonIP.ModelConfig.Notes
                };
            }
        }

        public MonitorIP()
        {

        }

        [Key]
        [DatabaseGeneratedAttribute(DatabaseGeneratedOption.Identity)]
        public int ID { get; set; }
        /// <summary>
        /// Is monitoring for the host enabled.
        /// </summary>
        public bool Enabled { get; set; }
        /// <summary>
        ///  The host address and hostname of the host.
        /// </summary>
        [MaxLength(512)]
        public string? Address { get; set; }
        /// <summary>
        /// The endpoint type. Endpoint types are: quantum is a quantum safe encryption test, http is a website ping, https is an SSL certificate check, httphtml is a website HTML load, icmp is a host ping, dns is a DNS lookup, smtp is an email server helo message confirmation, rawconnect is a low-level raw socket connection, nmap is a nmap service scan of the host, nmapvuln is a nmap vulnerability scan of the host and crawlsite performs a simulated user crawl of the site that generates site traffic using chrome browser.
        /// </summary>
        [MaxLength(50)]
        public string? EndPointType { get; set; }
        /// <summary>
        ///  The port of the service being monitored. It will be zero if it is the standard port for the host end point type.
        /// </summary>
        public ushort Port { get; set; }
        /// <summary>
        /// The time to wait for a timeout in milliseconds. If a timeout occurs the host is considered to be down or the test has failed.
        /// </summary>

        public int Timeout { get; set; }


        [JsonIgnore]
        public string? UserInfoUserID { get; set; }
        /// <summary>
        /// The user that has added this host.
        /// </summary>

        public string? UserID { get; set; }

        /// <summary>
        /// Is the host hidden. Ie it should no longer be visible or be being monitored.
        /// </summary>
        public bool Hidden { get; set; } = false;

        [MaxLength(50)]
        private string _appID = "";
        /// <summary>
        /// The Monitoring agent ID. For internet monitors this can be 1 for agent located at London - UK , 2 for Kansas - USA, 3 for Berlin - Germany. If it is a local agent then it will be the same as the user id .
        /// </summary>
        [MaxLength(255)]
        public string AppID { get => _appID; set => _appID = value; }
        /// <summary>
        /// Username used for authenticatin the service on the host.
        /// </summary>

        [MaxLength(512)]
        public string? Username { get; set; }

        private string? _password = null;
        /// <summary>
        /// Username used for authenticatin the service on the host.
        /// </summary>
        public string? Password { get => _password; set => _password = value; }

        /// <summary>
        /// Extra arguments for command-style endpoints.
        /// </summary>
        [MaxLength(2048)]
        public string? Args { get; set; }

        [ForeignKey("UserInfoUserID")]
        public virtual UserInfo? UserInfo { get; set; }
        /// <summary>
        /// When the host is down alerts are sent to this email address. When adding hosts every host must have an email address assigned to it. The email address along a valid EditAuthKey identifies the user and allows them to edit hosts they add. 
        /// </summary>
        [MaxLength(255)]
        public string? AddUserEmail { get; set; } = null;

        /// <summary>
        /// Has the User Email been verified. Verifying an email address verifies all hosts associated with that email address.
        /// </summary>
        public bool IsEmailVerified { get; set; } = false;
        /// <summary>
        /// This Authorization key is used to check if an Api request has the authority to edit hosts associated with a single email address. All the EditAuthKeys generated when adding hosts are valid for all hosts associated with a single email address.
        /// </summary>

        [MaxLength(512)]
        public string? EditAuthKey { get; set; } = null;

        /// <summary>
        /// This message should be displayed to the user. It will contain addtional information about the host.
        /// </summary>
        [NotMapped]
        public string? MessageForUser { get; set; }

        /// <summary>
        /// Date host was added.
        /// </summary>
        public DateTime? DateAdded { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "char(64)")]
        [MaxLength(64)]
        public string? SiteHash { get; set; } = null;

        public int? MonitorModelConfigId { get; set; }

        public MonitorModelConfig? ModelConfig { get; set; }

        [NotMapped]
        public string? Prompt { get; set; }

        /// <summary>
        /// The location of the monitoring agent for this host.
        /// </summary>
        [NotMapped]
        public string? AgentLocation { get; set; }
#pragma warning restore IL2026
    }
}
