using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace NetworkMonitor.Objects.Entity;

[PrimaryKey(nameof(ProcessorAppID), nameof(EndpointType), nameof(Type))]
public class EndpointMeasurement
{
    [MaxLength(255)] public string ProcessorAppID { get; set; } = "";
    [MaxLength(50)] public string EndpointType { get; set; } = "";
    [MaxLength(50)] public string Type { get; set; } = "";
    [MaxLength(32)] public string Unit { get; set; } = "ms";
    public double Scale { get; set; } = 1;
}
