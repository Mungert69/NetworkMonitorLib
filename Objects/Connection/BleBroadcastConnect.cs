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
        // This endpoint mixes selected readings and elapsed-time fallbacks.
        public override EndpointMeasurementMetadata Measurement => new(Unit: "raw value",
            Description: "BLE receipt measurement without an identified numeric metric.", AnalysisKind: "unspecified",
            AnalysisGuidance: "Describe availability only. Do not infer a voltage, temperature or other physical measurement without a selected metric.");
        public override IReadOnlyCollection<EndpointMeasurementMetadata> MeasurementVariants => new[]
        {
            new EndpointMeasurementMetadata("V", 0.01, "battery_voltage"),
            new EndpointMeasurementMetadata("V", 0.01, "battery_voltage_v"),
            new EndpointMeasurementMetadata("V", 0.01, "battery_v"),
            new EndpointMeasurementMetadata("A", 0.1, "battery_current"),
            new EndpointMeasurementMetadata("A", 0.1, "battery_current_a"),
            new EndpointMeasurementMetadata("A", 0.1, "battery_a"),
            new EndpointMeasurementMetadata("A", 0.1, "load_current"),
            new EndpointMeasurementMetadata("A", 0.1, "load_current_a"),
            new EndpointMeasurementMetadata("A", 0.1, "load_a"),
            new EndpointMeasurementMetadata("W", 1, "pv_power"),
            new EndpointMeasurementMetadata("W", 1, "pvpower"),
            new EndpointMeasurementMetadata("W", 1, "pv"),
            new EndpointMeasurementMetadata("kWh", 0.01, "yield_today"),
            new EndpointMeasurementMetadata("kWh", 0.01, "yield"),
            new EndpointMeasurementMetadata("kWh", 0.01, "yield_today_kwh"),
            new EndpointMeasurementMetadata("°C", 0.01, "temperature"),
            new EndpointMeasurementMetadata("°C", 0.01, "temperature_2"),
            new EndpointMeasurementMetadata("°C", 0.01, "temperature_3"),
            new EndpointMeasurementMetadata("°C", 0.01, "battery_temperature"),
            new EndpointMeasurementMetadata("°C", 0.01, "dewpoint"),
            new EndpointMeasurementMetadata("%", 0.01, "humidity"),
            new EndpointMeasurementMetadata("%", 0.01, "humidity_2"),
            new EndpointMeasurementMetadata("%", 0.01, "moisture"),
            new EndpointMeasurementMetadata("%", 1, "battery"),
            new EndpointMeasurementMetadata("min", 1, "time_to_go"),
            new EndpointMeasurementMetadata("Ah", 0.1, "consumed_ah"),
            new EndpointMeasurementMetadata("rpm", 1, "rotational_speed"),
            new EndpointMeasurementMetadata("s", 0.001, "duration")
        }.Select(m => m with {
            Description = "Legacy BLE " + m.Type.Replace('_', ' ') + " in " + m.Unit + ".",
            AnalysisKind = "unspecified",
            AnalysisGuidance = "Legacy metric encoding. Describe values and availability; do not infer operating limits or device-specific semantics."
        }).Concat(BleMetricCatalogue.Definitions.Select(d => d.Measurement)).ToArray();

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

            PreConnect();
            var result = new ResultObj();
            ushort responseTime = 0;

            try
            {
                string address = MpiStatic.Address.Trim();
                string key = MpiStatic.Password?.Trim() ?? "";

                string arguments = $"--address \"{address}\"";
                if (!string.IsNullOrWhiteSpace(key))
                {
                    arguments += $" --key \"{key}\"";
                }
                string extraArgs = MpiStatic.Args?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(extraArgs))
                {
                    extraArgs = MpiStatic.Username?.Trim() ?? "";
                }
                if (!string.IsNullOrWhiteSpace(extraArgs))
                {
                    arguments += $" {extraArgs}";
                }

                if (!TryGetMetricOptions(extraArgs, out var metric, out var explicitMetric, out var scale, out var offset, out var optionError))
                {
                    ProcessException(optionError, "BLE Metric Error");
                    return;
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
                    if (result.Data is BleDecodedPayload decoded)
                    {
                        string? format = BleMetricCatalogue.FormatFromArgs(extraArgs);
                        var encoding = explicitMetric && !scale.HasValue && offset == 0 && format != null ? BleMetricCatalogue.Find(format,metric) : null;
                        if (encoding != null) {
                            var values = decoded.Readings.Where(r => r.Metric == BleMetricSelector.Canonical(metric)).ToArray();
                            if(values.Length == 1 && values[0].Value.HasValue && encoding.TryEncode(values[0].Value!.Value,out var encoded))
                                ProcessStatus(encoding.Status,encoded,result.Message + $"\nAutomatic encoding: scale={encoding.Scale.ToString(CultureInfo.InvariantCulture)}; offset={encoding.Offset.ToString(CultureInfo.InvariantCulture)}");
                            else ProcessException($"BLE metric '{metric}' is missing, unavailable, ambiguous or outside its published range.\n{result.Message}","BLE Metric Error");
                        }
                        else if (explicitMetric && !scale.HasValue && offset == 0 && format != null)
                            ProcessException($"No automatic numeric metric definition for '{metric}'.\n{result.Message}","BLE Metric Error");
                        else if (BleMetricSelector.TrySelect(decoded.Readings, metric, scale, offset, out var sample, out var label, out var metricError))
                        {
                            var reading = decoded.Readings.Single(r => r.Metric == label);
                            string encodingDescription = $"Recorded metric: {label}; value={reading.Value!.Value.ToString("0.######", CultureInfo.InvariantCulture)} {reading.Unit}; sample={sample}; scale={(scale ?? BleMetricSelector.DefaultScale(reading)).ToString(CultureInfo.InvariantCulture)}; offset={offset.ToString(CultureInfo.InvariantCulture)}";
                            ProcessStatus($"BLE {label}", sample, result.Message + "\n" + encodingDescription);
                        }
                        else if (explicitMetric || decoded.Readings.Any(r => r.Metric == BleMetricSelector.Canonical(metric)))
                        {
                            ProcessException(metricError + "\n" + result.Message, "BLE Metric Error");
                        }
                        else ProcessStatus("BLE broadcast received", responseTime, result.Message);
                    }
                    else if (TryExtractMetricValue(result.Message, metric, out var metricValue, out var metricLabel))
                    {
                        // Compatibility with older/custom text-only command providers.
                        double defaultScale = metricLabel == "battery_voltage" || metricLabel == "yield_today" ? 100 : metricLabel == "pv_power" ? 1 : 10;
                        var reading = new BleReading(metricLabel, metricValue, "", 1 / defaultScale);
                        if (BleMetricSelector.TrySelect(new[] { reading }, metric, scale, offset, out var sample, out var label, out var metricError))
                            ProcessStatus($"BLE {label}", sample, result.Message);
                        else ProcessException(metricError, "BLE Metric Error");
                    }
                    else if (explicitMetric) ProcessException($"BLE metric '{metric}' is missing or unavailable.\n{result.Message}", "BLE Metric Error");
                    else ProcessStatus("BLE broadcast received", responseTime, result.Message);
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

        private static bool TryGetMetricOptions(string args, out string metric, out bool explicitMetric,
            out double? scale, out double offset, out string error)
        {
            metric = DefaultMetric; explicitMetric = false; scale = null; offset = 0; error = "";
            foreach (string name in new[] { "metric", "metric_scale", "metric_offset" })
            {
                var options = Regex.Matches(args, $@"(?:^|\s)--{name}(?==|\s|$)(?:=|\s+)?(?<value>[^\s]*)", RegexOptions.IgnoreCase);
                if (options.Count == 0) continue;
                string value = options[0].Groups["value"].Value.Trim('\"', '\'');
                if (options.Count != 1 || value.Length == 0 || value.StartsWith("--"))
                { error = $"Invalid or repeated --{name}."; return false; }
                if (name == "metric") { metric = BleMetricSelector.Canonical(value); explicitMetric = true; continue; }
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || (name == "metric_scale" && number <= 0))
                { error = $"--{name} must be a finite {(name == "metric_scale" ? "positive " : "")}number."; return false; }
                if (name == "metric_scale") scale = number; else offset = number;
            }
            if (!explicitMetric && (scale.HasValue || offset != 0)) { error = "Specify --metric when using metric scaling or offset."; return false; }
            return true;
        }

        private static bool TryExtractMetricValue(string output, string metric, out double value, out string label)
        {
            value = 0; label = BleMetricSelector.Canonical(metric);
            string? field = label switch
            {
                "pv_power" => "PV power", "battery_voltage" => "Battery voltage",
                "battery_current" => "Battery current", "load_current" => "Load current",
                "yield_today" => "Yield today", _ => null
            };
            if (field == null) return false;
            var match = Regex.Match(output, $@"(?:^|[;\r\n])\s*{Regex.Escape(field)}:\s*(?<val>[-+]?(?:\d+(?:\.\d*)?|\.\d+))(?:\s|;|$)", RegexOptions.IgnoreCase);
            return match.Success && double.TryParse(match.Groups["val"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        }
    }
}
