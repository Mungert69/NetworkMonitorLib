using System.ComponentModel.DataAnnotations;
namespace NetworkMonitor.DTOs;

public sealed class ChartRangeQuery
{
    [Range(1, int.MaxValue)] public int MonitorIPID { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
}

/// <summary>Chart-only response. Values and summary are already in physical units.</summary>
public sealed class ChartRangeResponse
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public string Unit { get; set; } = "ms";
    public string? Measurement { get; set; }
    public long Successful { get; set; }
    public long Failed { get; set; }
    public double? Average { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public bool IncludesLatest { get; set; }
    public List<ChartRangePoint> Points { get; set; } = new();
    public List<string> Notices { get; set; } = new();
}
public sealed record ChartRangePoint(DateTimeOffset Timestamp, double? Value, bool Success, string? Status);
