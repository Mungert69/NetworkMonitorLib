using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using System;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class BleBroadcastListenConnect : NetConnect, IBleCycleParticipant
    {
        private const int DurationScale = 10;
        public override EndpointMeasurementMetadata Measurement => new(Scale: DurationScale,
            Description: "Passive BLE advertisement capture, not a selected numeric sensor measurement.",
            AnalysisKind: "discovery", AnalysisGuidance: "Describe raw advertisement capture availability. Do not interpret processing duration as a sensor reading.");
        private long _lastSequence;
        internal BleAdvertisementSnapshot? CycleSnapshot { get; set; }
        public BleCaptureRequirement? CaptureRequirement => null;
        public void PrepareSnapshot(BleAdvertisementSnapshot snapshot) => CycleSnapshot = snapshot;
        protected override bool UsesOperationTimeout => false;
        // Raw listen reads a prepared cycle snapshot and has no timeout/window.
        public override void ConfigureTimeout(int defaultTimeout, bool clampToDefault = false) { }
        private readonly ICmdProcessor? _cmdProcessor;

        public BleBroadcastListenConnect(ICmdProcessorProvider? cmdProcessorProvider)
        {
            if (cmdProcessorProvider != null)
            {
                _cmdProcessor = cmdProcessorProvider.GetProcessor("BleBroadcastListen");
            }

            IsLongRunning = false;
        }

        public override async Task Connect()
        {
            var snapshot = CycleSnapshot;
            if (_cmdProcessor == null)
            {
                ProcessException("No Command Processor Available", "Error");
                return;
            }

            if (snapshot == null)
            {
                ProcessException("BLE cycle snapshot was not prepared", "BLE Error");
                return;
            }

            PreConnect();
            var result = new ResultObj();
            ushort responseTime = 0;

            try
            {
                string key = MpiStatic.Password?.Trim() ?? "";

                string arguments = "";
                if (!string.IsNullOrWhiteSpace(key))
                {
                    arguments = $"--key \"{key}\"";
                }

                string extraArgs = MpiStatic.Args?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(extraArgs))
                {
                    extraArgs = MpiStatic.Username?.Trim() ?? "";
                }
                if (!string.IsNullOrWhiteSpace(extraArgs))
                {
                    arguments = string.IsNullOrWhiteSpace(arguments)
                        ? extraArgs
                        : $"{arguments} {extraArgs}";
                }

                Timer.Reset();
                Timer.Start();
                var processorScanDataObj = new ProcessorScanDataObj
                {
                    Arguments = arguments,
                    SendMessage = false
                };
                long afterSequence = _lastSequence;
                var token = Cts.Token;
                result = _cmdProcessor is IBleListenSnapshotProcessor buffered
                    ? await Task.Run(() => buffered.ReadSnapshot(arguments, snapshot, afterSequence, token), token)
                    : await _cmdProcessor.QueueCommand(Cts, processorScanDataObj);
                if (result.Success && snapshot != null) _lastSequence = snapshot.LastSequence;
                Timer.Stop();

                if (result.Success)
                {
                    responseTime = (ushort)(Timer.ElapsedMilliseconds / DurationScale);
                    ProcessStatus("BLE listen complete", responseTime, result.Message);
                }
                else
                {
                    ProcessException(result.Message, "BLE Error");
                }
            }
            catch (Exception e)
            {
                ProcessException(e.Message, "Exception");
            }
            finally
            {
                PostConnect();
            }
        }
    }
}
