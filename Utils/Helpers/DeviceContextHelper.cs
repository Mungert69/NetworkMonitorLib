using Microsoft.Extensions.Logging;
using NetworkMonitor.Connection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkMonitor.Utils.Helpers
{
    public static class DeviceContextHelper
    {
        public static DeviceContext CaptureNetworkContext(ILogger? logger = null)
        {
            var context = new DeviceContext
            {
                CapturedAtUtc = DateTime.UtcNow,
                Hostname = Environment.MachineName ?? string.Empty,
                Platform = RuntimeInformation.OSDescription ?? string.Empty,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                TimeZone = TimeZoneInfo.Local.Id
            };

            var all = new List<DeviceNetworkInterfaceInfo>();
            var candidates = new List<(NetworkInterface nic, UnicastIPAddressInformation ip)>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var props = nic.GetIPProperties();
                var ip4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                var gateway = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address.ToString() ?? string.Empty;
                var mask = ip4?.IPv4Mask?.ToString() ?? string.Empty;
                var ip = ip4?.Address?.ToString() ?? string.Empty;
                var isUp = nic.OperationalStatus == OperationalStatus.Up;

                all.Add(new DeviceNetworkInterfaceInfo
                {
                    Name = nic.Name,
                    NetworkType = nic.NetworkInterfaceType.ToString(),
                    IPv4 = ip,
                    SubnetMask = mask,
                    Gateway = gateway,
                    IsUp = isUp
                });

                if (ip4 != null && isUp && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    candidates.Add((nic, ip4));
                }
            }

            context.Interfaces = all;

            var primary = candidates
                .OrderByDescending(c => c.nic.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                .ThenByDescending(c => c.nic.Speed)
                .FirstOrDefault();

            if (primary.nic != null)
            {
                var props = primary.nic.GetIPProperties();
                var gateway = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address.ToString() ?? string.Empty;

                context.PrimaryInterface = primary.nic.Name;
                context.PrimaryIPv4 = primary.ip.Address.ToString();
                context.SubnetMask = primary.ip.IPv4Mask?.ToString() ?? string.Empty;
                context.DefaultGateway = gateway;
                context.MacAddress = FormatMac(primary.nic.GetPhysicalAddress());
            }

            logger?.LogInformation(
                "Captured device network context host={Host} interface={Interface} ip={IP} gateway={Gateway}",
                context.Hostname,
                context.PrimaryInterface,
                context.PrimaryIPv4,
                context.DefaultGateway);

            return context;
        }

        public static async Task EnrichWithReverseGeocodeAsync(
            DeviceContext context,
            double latitude,
            double longitude,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                return;
            }

            context.Geo.Latitude = latitude;
            context.Geo.Longitude = longitude;

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(6);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkMonitorAgent/1.0");
                var lat = latitude.ToString(CultureInfo.InvariantCulture);
                var lon = longitude.ToString(CultureInfo.InvariantCulture);
                var url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}";
                using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("address", out var address))
                {
                    var town = GetJsonString(address, "town")
                        ?? GetJsonString(address, "city")
                        ?? GetJsonString(address, "village")
                        ?? GetJsonString(address, "hamlet")
                        ?? string.Empty;
                    var country = GetJsonString(address, "country") ?? string.Empty;

                    context.NearestTown = town;
                    context.Country = country;
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Reverse geocode failed.");
            }
        }

        public static string BuildMonitorLocation(DeviceContext? context, string fallbackLocation)
        {
            if (context == null)
            {
                return NormalizeNoComma(fallbackLocation);
            }

            if (!string.IsNullOrWhiteSpace(context.NearestTown) && !string.IsNullOrWhiteSpace(context.Country))
            {
                return NormalizeNoComma($"{context.NearestTown} {context.Country}");
            }

            if (!string.IsNullOrWhiteSpace(context.NearestTown))
            {
                return NormalizeNoComma(context.NearestTown);
            }

            if (!string.IsNullOrWhiteSpace(fallbackLocation))
            {
                return NormalizeNoComma(fallbackLocation);
            }

            return "UnknownLocation";
        }

        public static string BuildLlmDeviceContextSummary(DeviceContext? context, string fallbackLocation)
        {
            if (context == null)
            {
                return $"location={NormalizeNoComma(fallbackLocation)}";
            }

            var parts = new List<string>
            {
                $"location={BuildMonitorLocation(context, fallbackLocation)}",
                $"host={NormalizeNoComma(context.Hostname)}",
                $"platform={NormalizeNoComma(context.Platform)}"
            };

            if (!string.IsNullOrWhiteSpace(context.PrimaryIPv4))
            {
                parts.Add($"ip={context.PrimaryIPv4}");
            }
            if (!string.IsNullOrWhiteSpace(context.SubnetMask))
            {
                parts.Add($"mask={context.SubnetMask}");
            }
            if (!string.IsNullOrWhiteSpace(context.DefaultGateway))
            {
                parts.Add($"gateway={context.DefaultGateway}");
            }
            if (context.Geo.Latitude.HasValue && context.Geo.Longitude.HasValue)
            {
                parts.Add($"gps={context.Geo.Latitude.Value.ToString("F6", CultureInfo.InvariantCulture)}:{context.Geo.Longitude.Value.ToString("F6", CultureInfo.InvariantCulture)}");
            }
            if (!string.IsNullOrWhiteSpace(context.Geo.Source))
            {
                parts.Add($"geo_source={NormalizeNoComma(context.Geo.Source)}");
            }

            var summary = string.Join("; ", parts);
            if (summary.Length > 700)
            {
                summary = summary.Substring(0, 700);
            }
            return summary;
        }

        public static string EncodeHandshakeValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            return Convert.ToBase64String(bytes);
        }

        public static string DecodeHandshakeValue(string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded))
            {
                return string.Empty;
            }

            try
            {
                var bytes = Convert.FromBase64String(encoded);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return encoded;
            }
        }

        private static string FormatMac(PhysicalAddress address)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 0)
            {
                return string.Empty;
            }
            return string.Join(":", bytes.Select(b => b.ToString("X2")));
        }

        private static string? GetJsonString(JsonElement element, string key)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString();
        }

        private static string NormalizeNoComma(string value)
            => (value ?? string.Empty).Replace(",", " ").Trim();
    }
}
