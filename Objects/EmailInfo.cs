using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkMonitor.Objects;

public class EmailInfo
{
    [Key]
     public Guid ID { get; set; } = Guid.NewGuid(); // Initialize with a new GUID

    public string Email { get; set; }="";
    public DateTime DateSent { get; set; } = DateTime.UtcNow;
    public DateTime? DateOpened { get; set; } 
    public bool IsOpen { get; set; } = false;
    public string EmailType {get;set;}="";
}
