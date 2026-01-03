using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using System;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class BleBroadcastConnect : NetConnect
    {
        private readonly ICmdProcessor? _cmdProcessor;

        public BleBroadcastConnect(ICmdProcessorProvider? cmdProcessorProvider)
        {
            if (cmdProcessorProvider != null)
            {
                _cmdProcessor = cmdProcessorProvider.GetProcessor("BleBroadcast");
            }

            IsLongRunning = true;
        }

        public override async Task Connect()
        {
            ExtendTimeout = true;

            if (_cmdProcessor == null)
            {
                ProcessException("No Command Processor Available", "Error");
                return;
            }

            if (string.IsNullOrWhiteSpace(MpiStatic.Address))
            {
                ProcessException("Missing BLE address", "Error");
                return;
            }

            if (string.IsNullOrWhiteSpace(MpiStatic.Password))
            {
                ProcessException("Missing BLE key (use Password field)", "Error");
                return;
            }

            PreConnect();
            var result = new ResultObj();

            try
            {
                string address = MpiStatic.Address.Trim();
                string key = MpiStatic.Password.Trim();

                string arguments = $"--address \"{address}\" --key \"{key}\"";

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
                    ProcessStatus("BLE broadcast received", (ushort)Timer.ElapsedMilliseconds, result.Message);
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
