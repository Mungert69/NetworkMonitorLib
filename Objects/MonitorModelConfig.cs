using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkMonitor.Objects;

public class MonitorModelConfig
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ID { get; set; }

    public double? ChangeConfidence { get; set; }
    public double? SpikeConfidence { get; set; }
    public int? ChangePreTrain { get; set; }
    public int? SpikePreTrain { get; set; }
    public int? PredictWindow { get; set; }
    public int? SpikeDetectionThreshold { get; set; }

    public int? RunLength { get; set; }
    public int? KOfNK { get; set; }
    public int? KOfNN { get; set; }
    public double? MadAlpha { get; set; }
    public double? MinBandAbs { get; set; }
    public double? MinBandRel { get; set; }
    public int? RollSigmaWindow { get; set; }
    public int? BaselineWindow { get; set; }
    public int? SigmaCooldown { get; set; }
    public double? MinRelShift { get; set; }
    public int? SampleRows { get; set; }
    public double? NearMissFraction { get; set; }
    public bool? LogJson { get; set; }

    public DateTime? UpdatedUtc { get; set; }

    [MaxLength(255)]
    public string? UpdatedBy { get; set; }

    [MaxLength(512)]
    public string? Notes { get; set; }
}
