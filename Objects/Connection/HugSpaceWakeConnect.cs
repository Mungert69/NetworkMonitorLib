using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class HugSpaceWakeConnect : NetConnect
    {
        private readonly ICmdProcessor? _cmdProcessor;
        private readonly string _baseArg;

        public HugSpaceWakeConnect(ICmdProcessorProvider? cmdProcessorProvider, string baseArg)
        {
            if (cmdProcessorProvider != null)
                _cmdProcessor = cmdProcessorProvider.GetProcessor("HugSpaceWake");

            _baseArg = baseArg;
            IsLongRunning = true;
        }

        public override async Task Connect()
        {
            ExtendTimeout = true;
            ExtendTimeoutMultiplier = 20;

            if (_cmdProcessor == null)
            {
                ProcessException("No Command Processor Available", "Error");
                return;
            }

            PreConnect();
            ushort responseTime = 0;

            try
            {
                // Build absolute URL (fix scheme check: use AND, not OR)
                var address = MpiStatic.Address?.Trim() ?? string.Empty;

                // If no scheme provided, default to https
                if (!address.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    address = "https://" + address;
                }

                // Robust URL building with optional port
                UriBuilder uriBuilder;
                if (Uri.TryCreate(address, UriKind.Absolute, out var parsed))
                {
                    uriBuilder = new UriBuilder(parsed);
                }
                else
                {
                    uriBuilder = new UriBuilder(address);
                }

                if (MpiStatic.Port != 0)
                    uriBuilder.Port = MpiStatic.Port;

                var targetUrl = uriBuilder.Uri.AbsoluteUri;

                // Build CLI args for the processor
                string arguments = $"--url {targetUrl}";
                if (!string.IsNullOrWhiteSpace(_baseArg))
                    arguments += $" {_baseArg}";

                Timer.Reset();
                Timer.Start();

                var processorScanDataObj = new ProcessorScanDataObj
                {
                    Arguments = arguments,
                    SendMessage = false
                };

                var result = await _cmdProcessor.QueueCommand(Cts, processorScanDataObj);

                Timer.Stop();
                responseTime = (ushort)Timer.ElapsedMilliseconds;

                var output = result.Message ?? string.Empty;

                // Interpret result
                var (isHostUp, hostStatus) = GetCrawlStatus(output);

                // If the processor itself failed, treat as down
                if (!result.Success)
                    isHostUp = false;

                if (!isHostUp)
                {
                    // Use max value to indicate failure per your convention
                    responseTime = ushort.MaxValue;
                    ProcessException(output, hostStatus);
                }
                else
                {
                    ProcessStatus(hostStatus, responseTime, output);
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

        private static (bool isUp, string status) GetCrawlStatus(string input)
        {
            const string upPattern = @"Alive";
            const string downPattern = @"Error";

            if (Regex.IsMatch(input ?? string.Empty, upPattern, RegexOptions.IgnoreCase))
                return (true, "Hug Space is Alive");

            if (Regex.IsMatch(input ?? string.Empty, downPattern, RegexOptions.IgnoreCase))
                return (false, "Hug Space Keep Alive Failed");

            return (false, "Hug Space Keep Alive Status Unknown");
        }
    }
}
