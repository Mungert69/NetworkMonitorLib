#define NetworkMonitorProcessor
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NetworkMonitor.Utils;
namespace NetworkMonitor.Objects;

public class PredictStatus
{
    public PredictStatus() { }

      public PredictStatus(PredictStatus copy) { 
                ID=copy.ID;
                AlertFlag=copy.AlertFlag;
                AlertSent=copy.AlertSent;
                EventTime=copy.EventTime;
                MonitorPingInfoID=copy.MonitorPingInfoID;
                Message=copy.Message;
                ChangeDetectionResult=copy.ChangeDetectionResult;
                SpikeDetectionResult=copy.SpikeDetectionResult;
      }
    [Key]
    public int ID { get; set; }
    private DateTime? _eventTime;
    private string? _message = "";

    /// <summary>
    /// Has an alert been raised for this host since the last alert reset.
    /// </summary>
    public bool AlertFlag { get; set; }
    /// <summary>
    /// Has an alert message been sent to user for this host since the last alert reset.
    /// </summary>
    public bool AlertSent { get; set; }
    /// <summary>
    /// The UTC time of the last reponse event for the data set. 
    /// </summary>
    public DateTime? EventTime
    {
        get
        {
            if (_eventTime.HasValue)
            {
                return DateTime.SpecifyKind(_eventTime.Value, DateTimeKind.Utc);
            }
            return null;

        }
        set { _eventTime = value; }
    }
    public int MonitorPingInfoID { get; set; }
#pragma warning disable IL2026
    /// <summary>
    /// The status message for the last reponse event for the data set.
    /// </summary>
    [MaxLength(255)]
#pragma warning restore IL2026
    public string? Message
    {
        get { return _message; }
        set
        {
            if (value != null)
                _message = StringUtils.Truncate(value, 255);
            else _message = "";
        }
    }
    public DetectionResult ChangeDetectionResult { get; set; } = new DetectionResult();
    public DetectionResult SpikeDetectionResult { get; set; } = new DetectionResult();
  
   // Backing fields for IsUp and DownCount
    private bool _isUpDb;
    private int _downCountDb;

    [NotMapped] // Tells EF Core not to map this property to a database column
    public bool? IsUp
    {
        get { return !(ChangeDetectionResult.IsIssueDetected && SpikeDetectionResult.IsIssueDetected); }
        set { _isUpDb = value ?? false; } // Adjust this logic based on your needs
    }

    [Column("IsUp")]
    public bool IsUpDb
    {
        get { return _isUpDb; }
        set { _isUpDb = value; }
    }

    [NotMapped] // This property is computed, so we tell EF Core to ignore it
    public int DownCount
    {
        get { return (ChangeDetectionResult.IsIssueDetected && SpikeDetectionResult.IsIssueDetected) ? 1 : 0; }
        set { _downCountDb = value; } // You may need to adjust this setter based on how you want to use it
    }

    [Column("DownCount")]
    public int DownCountDb
    {
        get { return _downCountDb; }
        set { _downCountDb = value; }
    }

}



