using NetworkMonitor.Objects;
using NetworkMonitor.Objects.ServiceMessage;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NetworkMonitor.Connection
{
    public class BleBroadcastConnect : NetConnect
    {
        private readonly ICmdProcessor? _cmdProcessor;
        private const string DefaultMetric = "pv_power";

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
            ushort responseTime = 0;

            try
            {
                string address = MpiStatic.Address.Trim();
                string key = MpiStatic.Password.Trim();

                string arguments = $"--address \"{address}\" --key \"{key}\"";
                string extraArgs = MpiStatic.Args?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(extraArgs))
                {
                    extraArgs = MpiStatic.Username?.Trim() ?? "";
                }
                if (!string.IsNullOrWhiteSpace(extraArgs))
                {
                    arguments += $" {extraArgs}";
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
                    responseTime = (ushort)Timer.ElapsedMilliseconds;
                    string metric = GetMetricFromArgs(extraArgs);
                    if (TryExtractMetricValue(result.Message, metric, out var metricValue, out var metricLabel))
                    {
                        responseTime = metricValue;
                        ProcessStatus($"BLE {metricLabel}", responseTime, result.Message);
                    }
                    else
                    {
                        ProcessStatus("BLE broadcast received", responseTime, result.Message);
                    }
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

        private static string GetMetricFromArgs(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return DefaultMetric;
            }

            var match = Regex.Match(args, @"--metric(?:=|\s+)(?<value>[^\s]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups["value"].Value.Trim().ToLowerInvariant();
            }

            return DefaultMetric;
        }

        private static bool TryExtractMetricValue(string output, string metric, out ushort value, out string label)
        {
            value = 0;
            label = metric;
            metric = metric.Trim().ToLowerInvariant();

            if (metric == "pv_power" || metric == "pvpower" || metric == "pv")
            {
                if (TryMatchNumber(output, @"PV power:\s*(?<val>[-+]?\d+)", out var num))
                {
                    value = ClampUShort((int)num);
                    label = $"pv_power={value}W";
                    return true;
                }
                return false;
            }

            if (metric == "battery_voltage" || metric == "battery_voltage_v" || metric == "battery_v")
            {
                if (TryMatchNumber(output, @"Battery voltage:\s*(?<val>[-+]?\d+(\.\d+)?)", out var num))
                {
                    var scaled = (int)Math.Round(num * 100, MidpointRounding.AwayFromZero);
                    value = ClampUShort(scaled);
                    label = $"battery_voltage={num:F2}V";
                    return true;
                }
                return false;
            }

            if (metric == "battery_current" || metric == "battery_current_a" || metric == "battery_a")
            {
                if (TryMatchNumber(output, @"Battery current:\s*(?<val>[-+]?\d+(\.\d+)?)", out var num))
                {
                    var scaled = (int)Math.Round(num * 10, MidpointRounding.AwayFromZero);
                    value = ClampUShort(scaled);
                    label = $"battery_current={num:F1}A";
                    return true;
                }
                return false;
            }

            if (metric == "load_current" || metric == "load_current_a" || metric == "load_a")
            {
                if (TryMatchNumber(output, @"Load current:\s*(?<val>[-+]?\d+(\.\d+)?)", out var num))
                {
                    var scaled = (int)Math.Round(num * 10, MidpointRounding.AwayFromZero);
                    value = ClampUShort(scaled);
                    label = $"load_current={num:F1}A";
                    return true;
                }
                return false;
            }

            if (metric == "yield_today" || metric == "yield" || metric == "yield_today_kwh")
            {
                if (TryMatchNumber(output, @"Yield today:\s*(?<val>[-+]?\d+(\.\d+)?)", out var num))
                {
                    var scaled = (int)Math.Round(num * 100, MidpointRounding.AwayFromZero);
                    value = ClampUShort(scaled);
                    label = $"yield_today={num:F2}kWh";
                    return true;
                }
                return false;
            }

            return false;
        }

        private static bool TryMatchNumber(string text, string pattern, out double value)
        {
            value = 0;
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!match.Success) return false;

            var raw = match.Groups["val"].Value;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static ushort ClampUShort(int value)
        {
            if (value < 0) return 0;
            if (value > ushort.MaxValue) return ushort.MaxValue;
            return (ushort)value;
        }
    }
}
