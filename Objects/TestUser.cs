using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NetworkMonitor.Objects
{
    public class TestUser
    {
#pragma warning disable IL2026
        public TestUser(){}

      

        [MaxLength(50)]
        public string? UserID { get; set; }


        private bool _enabled = true;
        public bool Enabled { get => _enabled; set => _enabled = value; }

    [Key]
    [MaxLength(255)]
    public string Email { get; set; } = "";


        public DateTime? ActivatedDate { get; set; }

        public DateTime? InviteSentDate { get; set; }
        public DateTime? CancelAt { get; set; }

      #pragma warning restore IL2026
    }
}
