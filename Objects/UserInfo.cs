using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Objects
{
    public class UserInfo
    {
#pragma warning disable IL2026
        public UserInfo() { }

        public UserInfo(UserInfo other)
        {
            if (other != null)
            {
                SetFields(other);
            }
        }

        public void SetFields(UserInfo other)
        {

            UserID = other.UserID;
            DateCreated = other.DateCreated;
            HostLimit = other.HostLimit;
            DisableEmail = other.DisableEmail;
            Status = other.Status;
            Name = other.Name;
            Given_name = other.Given_name;
            Family_name = other.Family_name;
            Nickname = other.Nickname;
            Sub = other.Sub;
            Enabled = other.Enabled;
            AccountType = other.AccountType;
            Email = other.Email;
            Email_verified = other.Email_verified;
            Picture = other.Picture;
            Updated_at = other.Updated_at;
            LastLoginDate = other.LastLoginDate;
            CustomerId = other.CustomerId;
            CancelAt = other.CancelAt;
            MonitorAlertEnabled = other.MonitorAlertEnabled;
            PredictAlertEnabled = other.PredictAlertEnabled;
            TokensUsed = other.TokensUsed;

        }


        [Key]
        [MaxLength(50)]
        public string? UserID { get; set; }

        private DateTime _dateCreated;
        public DateTime DateCreated
        {
            get
            {
                return DateTime.SpecifyKind(_dateCreated, DateTimeKind.Utc); ;
            }
            set { _dateCreated = value; }
        }

        public int HostLimit { get; set; }

        public bool DisableEmail { get; set; }
        [MaxLength(50)]
        public string? Status { get; set; } = "";
        [MaxLength(50)]
        public string? Name { get; set; } = "";
        [MaxLength(50)] public string? Given_name { get; set; } = "";
        [MaxLength(50)] public string? Family_name { get; set; } = "";
        [MaxLength(50)] public string? Nickname { get; set; } = "";
        [MaxLength(50)] public string? Sub { get; set; } = "";

        public bool Enabled { get; set; } = true;
        public bool MonitorAlertEnabled { get; set; } = true;
        public bool PredictAlertEnabled { get; set; } = false;

        [MaxLength(50)] public string? AccountType { get; set; } = "Default";

        [MaxLength(255)] public string? Email { get; set; } = "";

        public bool Email_verified { get; set; }

        [MaxLength(512)] public string? Picture { get; set; } = "";
        private DateTime _updated_at;
        public DateTime Updated_at
        {
            get
            {
                return DateTime.SpecifyKind(_updated_at, DateTimeKind.Utc); ;
            }
            set { _updated_at = value; }
        }
        private DateTime _lastLoginDate;
        public DateTime LastLoginDate
        {
            get
            {
                return DateTime.SpecifyKind(_lastLoginDate, DateTimeKind.Utc); ;
            }
            set { _lastLoginDate = value; }
        }

        [MaxLength(100)]
        public string? CustomerId { get; set; } = "";

        public DateTime? CancelAt { get; set; }

        private List<MonitorIP> _monitorIPs = new List<MonitorIP>();

        [JsonIgnore]
        public List<MonitorIP> MonitorIPs { get => _monitorIPs; set => _monitorIPs = value; }

        private int _tokensUsed;
        public int TokensUsed { get => _tokensUsed; set => _tokensUsed = value; }

        // Add this to your UserInfo class
private LoadServer _loadServer= new();

[NotMapped] 
public LoadServer LoadServer 
{ 
    get => _loadServer; 
    set => _loadServer = value; 
} 
#pragma warning restore IL2026
    }
}
