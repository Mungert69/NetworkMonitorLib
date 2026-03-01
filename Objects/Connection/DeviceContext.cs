using System;
using System.Collections.Generic;

namespace NetworkMonitor.Connection
{
    public class DeviceContext
    {
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
        public string CaptureSource { get; set; } = "unknown";
        public string Hostname { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string TimeZone { get; set; } = "UTC";
        public string PrimaryInterface { get; set; } = string.Empty;
        public string PrimaryIPv4 { get; set; } = string.Empty;
        public string SubnetMask { get; set; } = string.Empty;
        public string DefaultGateway { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public bool HasGps { get; set; }
        public string NearestTown { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public DeviceGeoContext Geo { get; set; } = new DeviceGeoContext();
        public List<DeviceNetworkInterfaceInfo> Interfaces { get; set; } = new List<DeviceNetworkInterfaceInfo>();
    }

    public class DeviceGeoContext
    {
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? AccuracyMeters { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public class DeviceNetworkInterfaceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string NetworkType { get; set; } = string.Empty;
        public string IPv4 { get; set; } = string.Empty;
        public string SubnetMask { get; set; } = string.Empty;
        public string Gateway { get; set; } = string.Empty;
        public bool IsUp { get; set; }
    }
}
