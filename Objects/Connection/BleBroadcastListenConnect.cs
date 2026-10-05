using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using System;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class BleBroadcastListenConnect : NetConnect
    {
        private const int DurationScale = 10;
        public override EndpointMeasurementMetadata Measurement => new(Scale: DurationScale,
            Description: "Passive BLE advertisement capture, not a selected numeric sensor measurement.",
            AnalysisKind: "discovery", AnalysisGuidance: "Describe capture availability and decode status. Do not interpret scan duration as a sensor reading.");
        private readonly ICmdProcessor? _cmdProcessor;

        public BleBroadcastListenConnect(ICmdProcessorProvider? cmdProcessorProvider)
        {
            ExtendTimeout = true;
            ExtendTimeoutMultiplier = DurationScale;

            if (cmdProcessorProvider != null)
            {
                _cmdProcessor = cmdProcessorProvider.GetProcessor("BleBroadcastListen");
            }

            IsLongRunning = true;
        }

        public override async Task Connect()
        {
            if (_cmdProcessor == null)
            {
                ProcessException("No Command Processor Available", "Error");
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
                result = await _cmdProcessor.QueueCommand(Cts, processorScanDataObj);
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
