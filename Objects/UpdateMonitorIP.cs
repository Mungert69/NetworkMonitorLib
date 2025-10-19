using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkMonitor.Objects
{
    public class UpdateMonitorIP : MonitorIP
    {
     
        private MonitorPingInfo? _monitorPingInfo=new MonitorPingInfo();
        private bool _deleteAll;
        private bool _isSwapping;
        private bool _delete;

        public UpdateMonitorIP()
        {
        }

        public UpdateMonitorIP(MonitorIP m)
        {
            AppID=m.AppID;
            Address=m.Address;
            Enabled=m.Enabled;
            EndPointType= m.EndPointType;
            Hidden= m.Hidden;
            ID= m.ID;
            Timeout= m.Timeout;
            UserID= m.UserID;
            Port= m.Port;
            AddUserEmail=m.AddUserEmail;
            IsEmailVerified=m.IsEmailVerified;
            Username=m.Username;
            Password=m.Password;
            MonitorModelConfigId = m.MonitorModelConfigId;
            if (m.ModelConfig != null)
            {
                ModelConfig = new MonitorModelConfig
                {
                    ChangeConfidence = m.ModelConfig.ChangeConfidence,
                    SpikeConfidence = m.ModelConfig.SpikeConfidence,
                    ChangePreTrain = m.ModelConfig.ChangePreTrain,
                    SpikePreTrain = m.ModelConfig.SpikePreTrain,
                    PredictWindow = m.ModelConfig.PredictWindow,
                    SpikeDetectionThreshold = m.ModelConfig.SpikeDetectionThreshold,
                    RunLength = m.ModelConfig.RunLength,
                    KOfNK = m.ModelConfig.KOfNK,
                    KOfNN = m.ModelConfig.KOfNN,
                    MadAlpha = m.ModelConfig.MadAlpha,
                    MinBandAbs = m.ModelConfig.MinBandAbs,
                    MinBandRel = m.ModelConfig.MinBandRel,
                    RollSigmaWindow = m.ModelConfig.RollSigmaWindow,
                    BaselineWindow = m.ModelConfig.BaselineWindow,
                    SigmaCooldown = m.ModelConfig.SigmaCooldown,
                    MinRelShift = m.ModelConfig.MinRelShift,
                    SampleRows = m.ModelConfig.SampleRows,
                    NearMissFraction = m.ModelConfig.NearMissFraction,
                    LogJson = m.ModelConfig.LogJson,
                    ChangeRunLength = m.ModelConfig.ChangeRunLength,
                    ChangeKOfNK = m.ModelConfig.ChangeKOfNK,
                    ChangeKOfNN = m.ModelConfig.ChangeKOfNN,
                    ChangeMadAlpha = m.ModelConfig.ChangeMadAlpha,
                    ChangeMinBandAbs = m.ModelConfig.ChangeMinBandAbs,
                    ChangeMinBandRel = m.ModelConfig.ChangeMinBandRel,
                    ChangeRollSigmaWindow = m.ModelConfig.ChangeRollSigmaWindow,
                    ChangeBaselineWindow = m.ModelConfig.ChangeBaselineWindow,
                    ChangeSigmaCooldown = m.ModelConfig.ChangeSigmaCooldown,
                    ChangeMinRelShift = m.ModelConfig.ChangeMinRelShift,
                    ChangeSampleRows = m.ModelConfig.ChangeSampleRows,
                    ChangeNearMissFraction = m.ModelConfig.ChangeNearMissFraction,
                    ChangeLogJson = m.ModelConfig.ChangeLogJson,
                    SpikeRunLength = m.ModelConfig.SpikeRunLength,
                    SpikeKOfNK = m.ModelConfig.SpikeKOfNK,
                    SpikeKOfNN = m.ModelConfig.SpikeKOfNN,
                    SpikeMadAlpha = m.ModelConfig.SpikeMadAlpha,
                    SpikeMinBandAbs = m.ModelConfig.SpikeMinBandAbs,
                    SpikeMinBandRel = m.ModelConfig.SpikeMinBandRel,
                    SpikeRollSigmaWindow = m.ModelConfig.SpikeRollSigmaWindow,
                    SpikeBaselineWindow = m.ModelConfig.SpikeBaselineWindow,
                    SpikeSigmaCooldown = m.ModelConfig.SpikeSigmaCooldown,
                    SpikeMinRelShift = m.ModelConfig.SpikeMinRelShift,
                    SpikeSampleRows = m.ModelConfig.SpikeSampleRows,
                    SpikeNearMissFraction = m.ModelConfig.SpikeNearMissFraction,
                    SpikeLogJson = m.ModelConfig.SpikeLogJson,
                    UpdatedUtc = m.ModelConfig.UpdatedUtc,
                    UpdatedBy = m.ModelConfig.UpdatedBy,
                    Notes = m.ModelConfig.Notes
                };
            }
        }

        public MonitorPingInfo? MonitorPingInfo { get => _monitorPingInfo; set => _monitorPingInfo = value; }
        public bool DeleteAll { get => _deleteAll; set => _deleteAll = value; }
        public bool IsSwapping { get => _isSwapping; set => _isSwapping = value; }
        public bool Delete { get => _delete; set => _delete = value; }
    }
}
