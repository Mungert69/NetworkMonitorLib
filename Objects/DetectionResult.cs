using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace NetworkMonitor.Objects;
public class DetectionResult
{
    public bool IsIssueDetected { get; set; }
    public int NumberOfDetections { get; set; }
    public bool IsDataLimited { get; set; } = false;
    public double AverageScore { get; set; }
    public double MinPValue { get; set; }
    public double MaxMartingaleValue { get; set; }
    public int IndexOfFirstDetection { get; set; }
    [NotMapped]
    public ResultObj Result { get; set; } = new ResultObj();
    // Additional fields as required
}
