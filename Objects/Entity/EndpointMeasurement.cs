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
    public double Offset { get; set; } = 0;
    [MaxLength(512)] public string Description { get; set; } = "";
    [MaxLength(32)] public string AnalysisKind { get; set; } = "";
    [MaxLength(2048)] public string AnalysisGuidance { get; set; } = "";

    public double? TimingExcellent { get; set; }
    public double? TimingGood { get; set; }
    public double? TimingFair { get; set; }
}
