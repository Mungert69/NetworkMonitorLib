namespace NetworkMonitor.Objects;
  public class RootObject
    {
        public Event @event { get; set; } = new Event();
    }

    public class Event
    {
        public string ApplicationId { get; set; } = "";
        public string AuthenticationType { get; set; } = "";
        public string ConnectorId { get; set; } = "";
        public long CreateInstant { get; set; }
        public string Id { get; set; } = "";
        public Info? Info { get; set; }
        public string IpAddress { get; set; } = "";
        public string Type { get; set; } = "";
        public User? User { get; set; }
    }

    public class Info
    {
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
    }

    public class User
    {
        public bool Active { get; set; }
    public string BreachedPasswordStatus { get; set; } = "";
    public string ConnectorId { get; set; } = "";
        public object? Data { get; set; } // This can be any type based on your data
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Id { get; set; } = "";
    public string ImageUrl { get; set; } = "";
        public long InsertInstant { get; set; }
        public long LastLoginInstant { get; set; }
    public string LastName { get; set; } = "";
        public long LastUpdateInstant { get; set; }
        public List<object>? Memberships { get; set; } // This can be any type based on your data
        public bool PasswordChangeRequired { get; set; }
        public long PasswordLastUpdateInstant { get; set; }
        public List<object>? PreferredLanguages { get; set; } // This can be any type based on your data
        public List<object>? Registrations { get; set; } // This can be any type based on your data
    public string TenantId { get; set; } = "";
        public TwoFactor? TwoFactor { get; set; }
    public string UsernameStatus { get; set; } = "";
        public bool Verified { get; set; }
    }
    public class TwoFactor
    {
        public List<object>? Methods { get; set; } // This can be any type based on your data
        public List<object>? RecoveryCodes { get; set; } // This can be any type based on your data
    }
