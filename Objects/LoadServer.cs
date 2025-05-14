using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkMonitor.Objects
{
    public class LoadServer
    {
#pragma warning disable IL2026
        public LoadServer() { }
        [Key]
        public int ID { get; set; }

        private string? _url;
        // Getter setter for _url which maxlength 512 characters.
        [MaxLength(512)]
        public string? Url
        {
            get
            {
                return _url;
            }
            set
            {
                if (value != null && value.Length > 512)
                {
                    _url = value.Substring(0, 512);
                }
                else
                {
                    _url = value;
                }
            }
        }

        private string? _userID;

        // Getter and setter for _userID which maxlength 50 characters.
        [MaxLength(50)]
        public string? UserID
        {
            get
            {
                return _userID;
            }
            set
            {
                if (value != null && value.Length > 50)
                {
                    _userID = value.Substring(0, 50);
                }
                else
                {
                    _userID = value;
                }
            }
        }

        private string? _rabbitHostName;

        // Getter and setter for _rabbitHostName which maxlength 50 characters.
        [MaxLength(50)]
        public string? RabbitHostName
        {
            get
            {
                return _rabbitHostName;
            }
            set
            {
                if (value != null && value.Length > 50)
                {
                    _rabbitHostName = value.Substring(0, 50);
                }
                else
                {
                    _rabbitHostName = value;
                }
            }
        }


        private ushort _rabbitPort;
        public ushort RabbitPort { get => _rabbitPort; set => _rabbitPort = value; }

        private string? _rabbitInstanceName;
        // Getter and setter for _rabbitInstanceName which maxlength 50 characters.
        [MaxLength(50)]
        public string? RabbitInstanceName
        {
            get
            {
                return _rabbitInstanceName;
            }
            set
            {
                if (value != null && value.Length > 50)
                {
                    _rabbitInstanceName = value.Substring(0, 50);
                }
                else
                {
                    _rabbitInstanceName = value;
                }
            }
        }

         private string? _countryCode;
        [MaxLength(2)]
        public string? CountryCode
        {
            get
            {
                return _countryCode;
            }
            set
            {
                if (value != null && value.Length > 2)
                {
                    _countryCode = value.Substring(0, 2);
                }
                else
                {
                    _countryCode = value;
                }
            }
        }

        private string? _region;
        [MaxLength(50)]
        public string? Region
        {
            get
            {
                return _region;
            }
            set
            {
                if (value != null && value.Length > 50)
                {
                    _region = value.Substring(0, 50);
                }
                else
                {
                    _region = value;
                }
            }
        }
        public void SetFields(LoadServer other)
    {
      if (other != null)
      {
        ID = other.ID;
        Url = other.Url;
        UserID = other.UserID;
        RabbitHostName = other.RabbitHostName;
        RabbitPort = other.RabbitPort;
        RabbitInstanceName = other.RabbitInstanceName;
        CountryCode = other.CountryCode;
        Region = other.Region;
      }
    }


#pragma warning restore IL2026
    }
}