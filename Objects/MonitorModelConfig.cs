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

    public int? ChangeRunLength { get; set; }
    public int? ChangeKOfNK { get; set; }
    public int? ChangeKOfNN { get; set; }
    public double? ChangeMadAlpha { get; set; }
    public double? ChangeMinBandAbs { get; set; }
    public double? ChangeMinBandRel { get; set; }
    public int? ChangeRollSigmaWindow { get; set; }
    public int? ChangeBaselineWindow { get; set; }
    public int? ChangeSigmaCooldown { get; set; }
    public double? ChangeMinRelShift { get; set; }
    public int? ChangeSampleRows { get; set; }
    public double? ChangeNearMissFraction { get; set; }
    public bool? ChangeLogJson { get; set; }

    public int? SpikeRunLength { get; set; }
    public int? SpikeKOfNK { get; set; }
    public int? SpikeKOfNN { get; set; }
    public double? SpikeMadAlpha { get; set; }
    public double? SpikeMinBandAbs { get; set; }
    public double? SpikeMinBandRel { get; set; }
    public int? SpikeRollSigmaWindow { get; set; }
    public int? SpikeBaselineWindow { get; set; }
    public int? SpikeSigmaCooldown { get; set; }
    public double? SpikeMinRelShift { get; set; }
    public int? SpikeSampleRows { get; set; }
    public double? SpikeNearMissFraction { get; set; }
    public bool? SpikeLogJson { get; set; }

    public DateTime? UpdatedUtc { get; set; }

    [MaxLength(255)]
    public string? UpdatedBy { get; set; }

    [MaxLength(512)]
    public string? Notes { get; set; }
}
