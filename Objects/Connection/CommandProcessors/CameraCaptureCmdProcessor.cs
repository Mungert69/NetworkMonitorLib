using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Connection
{
    public sealed class CameraCaptureCmdProcessor : CmdProcessor
    {
        private const int DefaultLongEdge = 1024;
        private const int HighDetailLongEdge = 1280;
        private const int JpegQuality = 4; // ffmpeg scale where lower is better quality

        public CameraCaptureCmdProcessor(
            ILogger logger,
            ILocalCmdProcessorStates cmdProcessorStates,
            IRabbitRepo rabbitRepo,
            NetConnectConfig netConfig)
            : base(logger, cmdProcessorStates, rabbitRepo, netConfig)
        {
        }

        public override async Task<ResultObj> RunCommand(
            string arguments,
            CancellationToken cancellationToken,
            ProcessorScanDataObj? processorScanDataObj = null)
        {
            var result = new ResultObj { Success = false };
            if (!_cmdProcessorStates.IsCmdAvailable)
            {
                result.Message = "Camera capture command is not available on this agent.";
                return result;
            }

            var args = ParseArguments(arguments);
            string protocol = GetArg(args, "protocol", "rtsp").ToLowerInvariant();
            string address = GetArg(args, "address", "");
            string username = GetArg(args, "username", "");
            string password = GetArg(args, "password", "");
            bool highDetail = ParseBool(GetArg(args, "high_detail", "false"));
            int longEdge = highDetail ? HighDetailLongEdge : DefaultLongEdge;
            string ffmpegPath = ResolveFfmpegPath(GetArg(args, "ffmpeg_path", "ffmpeg"));
            string profileToken = GetArg(args, "profile_token", "Profile_1");
            string rtspPath = GetArg(args, "rtsp_path", "");
            int? rtspPort = ParsePort(GetArg(args, "rtsp_port", ""));
            int? onvifPort = ParsePort(GetArg(args, "onvif_port", ""));
            bool allowInsecureTls = ParseBool(GetArg(args, "allow_insecure_tls", "true"));

            if (string.IsNullOrWhiteSpace(address))
            {
                result.Message = "Missing required parameter 'address'.";
                return result;
            }

            try
            {
                var capture = await CaptureImageAsync(
                    protocol,
                    address,
                    username,
                    password,
                    rtspPath,
                    rtspPort,
                    profileToken,
                    onvifPort,
                    ffmpegPath,
                    longEdge,
                    allowInsecureTls,
                    cancellationToken);

                if (!capture.Success || capture.ImageBytes == null || capture.ImageBytes.Length == 0)
                {
                    result.Message = capture.ErrorMessage ?? "Failed to capture camera image.";
                    return result;
                }

                var imageBytes = capture.ImageBytes;
                var sha = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
                var summary = new
                {
                    status = "success",
                    source = capture.Source,
                    protocol,
                    warning = capture.WarningMessage,
                    width_target = longEdge,
                    jpeg_quality = 80,
                    bytes = imageBytes.Length,
                    mime_type = "image/jpeg",
                    raw_data_encoding = "base64",
                    raw_data_sha256 = sha
                };

                if (processorScanDataObj != null)
                {
                    processorScanDataObj.ScanCommandSuccess = true;
                    processorScanDataObj.ScanCommandOutput = JsonSerializer.Serialize(summary);
                    processorScanDataObj.RawData = imageBytes;
                    processorScanDataObj.RawDataMimeType = "image/jpeg";
                    processorScanDataObj.RawDataEncoding = "base64";
                    processorScanDataObj.RawDataLength = imageBytes.Length;
                    processorScanDataObj.RawDataSha256 = sha;
                }

                result.Success = true;
                result.Message = JsonSerializer.Serialize(summary);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Camera capture failed: {ex.Message}";
                return result;
            }
        }

        public override string GetCommandHelp()
        {
            return @"
CameraCapture cmd processor captures a single camera frame and returns JPEG bytes via RawData.

Required:
- --address <ip/hostname/url>

Optional:
- --protocol rtsp|onvif (default: rtsp)
- --username <value>
- --password <value>
- --high_detail true|false (default: false; false=1024 long edge, true=1280)
- --profile_token <ONVIF profile token> (default: Profile_1)
- --rtsp_path <path> when address is host-only
- --rtsp_port <port> optional RTSP port override
- --onvif_port <port> optional ONVIF HTTP(S) port override
- --ffmpeg_path <path> (default: ffmpeg)
- --allow_insecure_tls true|false (default: true; for ONVIF HTTPS/self-signed certs)

Examples:
- --protocol rtsp --address 192.168.1.10 --username admin --password pass
- --protocol onvif --address 192.168.1.20 --username admin --password pass --profile_token Profile_1
";
        }

        private string ResolveFfmpegPath(string requestedPath)
        {
            if (!string.Equals(requestedPath, "ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                return requestedPath;
            }

            if (!string.IsNullOrWhiteSpace(_netConfig.NativeLibDir))
            {
                return Path.Combine(_netConfig.NativeLibDir, "libffmpeg_exec.so");
            }

            if (!string.IsNullOrWhiteSpace(_netConfig.CommandPath))
            {
                bool isWindows = string.Equals(_netConfig.OSPlatform, "windows", StringComparison.OrdinalIgnoreCase);
                string fileName = isWindows ? "ffmpeg.exe" : "ffmpeg";
                string candidate = Path.Combine(_netConfig.CommandPath, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return requestedPath;
        }

        private static string GetArg(Dictionary<string, string> args, string key, string defaultValue)
        {
            return args.TryGetValue(key, out var value) ? value : defaultValue;
        }

        private static bool ParseBool(string value)
        {
            if (bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int? ParsePort(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) &&
                port > 0 &&
                port <= 65535)
            {
                return port;
            }

            return null;
        }

        private sealed class CaptureResult
        {
            public bool Success { get; set; }
            public byte[]? ImageBytes { get; set; }
            public string? ErrorMessage { get; set; }
            public string? WarningMessage { get; set; }
            public string Source { get; set; } = "";
        }

        private async Task<CaptureResult> CaptureImageAsync(
            string protocol,
            string address,
            string username,
            string password,
            string rtspPath,
            int? rtspPort,
            string profileToken,
            int? onvifPort,
            string ffmpegPath,
            int longEdge,
            bool allowInsecureTls,
            CancellationToken cancellationToken)
        {
            if (string.Equals(protocol, "onvif", StringComparison.OrdinalIgnoreCase))
            {
                var snapshotResult = await ResolveOnvifSnapshotUriAsync(address, username, password, profileToken, onvifPort, allowInsecureTls, cancellationToken);
                if (!snapshotResult.Success || string.IsNullOrWhiteSpace(snapshotResult.Source))
                {
                    return snapshotResult;
                }

                var ffmpegResult = await CaptureStillWithFfmpegAsync(snapshotResult.Source, username, password, ffmpegPath, longEdge, cancellationToken, isRtsp: false, rtspPort: null);
                if (ffmpegResult.Success)
                {
                    return ffmpegResult;
                }

                var onvifFallbackResult = await CaptureStillFromHttpAsync(snapshotResult.Source, username, password, allowInsecureTls, cancellationToken);
                if (onvifFallbackResult.Success)
                {
                    onvifFallbackResult.WarningMessage = "ffmpeg capture failed; used ONVIF HTTP snapshot fallback.";
                    return onvifFallbackResult;
                }

                onvifFallbackResult.ErrorMessage = $"{ffmpegResult.ErrorMessage} ONVIF HTTP snapshot fallback failed: {onvifFallbackResult.ErrorMessage}".Trim();
                return onvifFallbackResult;
            }

            var rtspUrl = BuildRtspUrl(address, username, password, rtspPath, rtspPort);
            var ffmpegRtspResult = await CaptureStillWithFfmpegAsync(rtspUrl, username, password, ffmpegPath, longEdge, cancellationToken, isRtsp: true, rtspPort: rtspPort);
            if (ffmpegRtspResult.Success)
            {
                return ffmpegRtspResult;
            }

            var snapshotFallback = await ResolveOnvifSnapshotUriAsync(address, username, password, profileToken, onvifPort, allowInsecureTls, cancellationToken);
            if (!snapshotFallback.Success || string.IsNullOrWhiteSpace(snapshotFallback.Source))
            {
                return new CaptureResult
                {
                    Success = false,
                    ErrorMessage = $"{ffmpegRtspResult.ErrorMessage} ONVIF fallback failed: {snapshotFallback.ErrorMessage}".Trim()
                };
            }

            var onvifRtspFallback = await CaptureStillFromHttpAsync(snapshotFallback.Source, username, password, allowInsecureTls, cancellationToken);
            if (onvifRtspFallback.Success)
            {
                onvifRtspFallback.WarningMessage = "ffmpeg capture failed; used ONVIF HTTP snapshot fallback.";
                return onvifRtspFallback;
            }

            onvifRtspFallback.ErrorMessage = $"{ffmpegRtspResult.ErrorMessage} ONVIF HTTP snapshot fallback failed: {onvifRtspFallback.ErrorMessage}".Trim();
            return onvifRtspFallback;
        }

        private async Task<CaptureResult> ResolveOnvifSnapshotUriAsync(
            string address,
            string username,
            string password,
            string profileToken,
            int? onvifPort,
            bool allowInsecureTls,
            CancellationToken cancellationToken)
        {
            var result = new CaptureResult { Success = false };

            var handler = CreateCameraHttpClientHandler(username, password, allowInsecureTls);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            var candidateUris = await DiscoverOnvifMediaServiceUrisAsync(client, address, onvifPort, username, password, cancellationToken);
            string[] tokenCandidates = new[] { profileToken };
            int attemptedEndpoints = 0;
            bool snapshotActionNotSupported = false;
            var endpointDiagnostics = new List<string>();
            var allTokens = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(profileToken))
            {
                allTokens.Add(profileToken);
            }

            foreach (var mediaUri in candidateUris)
            {
                attemptedEndpoints++;
                try
                {
                    var capabilitiesResult = await DiscoverOnvifMediaServiceCapabilitiesAsync(client, mediaUri, username, password, cancellationToken);
                    if (capabilitiesResult.Capabilities != null)
                    {
                        endpointDiagnostics.Add(
                            $"endpoint={mediaUri}; auth={FormatAuthMode(capabilitiesResult.Response)}; snapshot_uri={FormatBool(capabilitiesResult.Capabilities.SnapshotUri)}; " +
                            $"rtsp={FormatBool(!capabilitiesResult.Capabilities.NoRtspStreaming)}; rtp_rtsp_tcp={FormatBool(capabilitiesResult.Capabilities.RtpRtspTcp)}; " +
                            $"max_profiles={FormatInt(capabilitiesResult.Capabilities.MaximumNumberOfProfiles)}");

                        if (capabilitiesResult.Capabilities.SnapshotUri == false)
                        {
                            snapshotActionNotSupported = true;
                        }
                    }
                    else
                    {
                        endpointDiagnostics.Add(
                            $"endpoint={mediaUri}; auth={FormatAuthMode(capabilitiesResult.Response)}; media_capabilities=unavailable; status={(int)capabilitiesResult.Response.StatusCode}");
                    }

                    var profileDiscovery = await DiscoverOnvifProfileTokensAsync(client, mediaUri, profileToken, username, password, cancellationToken);
                    if (profileDiscovery.Tokens.Count > 0)
                    {
                        tokenCandidates = profileDiscovery.Tokens.ToArray();
                        foreach (var token in profileDiscovery.Tokens)
                        {
                            allTokens.Add(token);
                        }
                    }
                }
                catch
                {
                }

                foreach (var token in tokenCandidates)
                {
                    try
                    {
                        string operationXml = BuildGetSnapshotUriOperationXml(token);
                        var response = await PostOnvifSoapAsync(client, mediaUri, operationXml, username, password, cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            if (IsOnvifActionNotSupportedFault(response.Xml))
                            {
                                snapshotActionNotSupported = true;
                            }
                            endpointDiagnostics.Add(
                                $"snapshot_uri_attempt endpoint={mediaUri}; token={token}; auth={FormatAuthMode(response)}; status={(int)response.StatusCode}; fault={GetOnvifFaultKind(response.Xml)}");
                            continue;
                        }

                        var snapshotUri = ExtractSnapshotUri(response.Xml);
                        if (string.IsNullOrWhiteSpace(snapshotUri))
                        {
                            endpointDiagnostics.Add(
                                $"snapshot_uri_attempt endpoint={mediaUri}; token={token}; auth={FormatAuthMode(response)}; result=empty_uri");
                            continue;
                        }

                        if (Uri.TryCreate(snapshotUri, UriKind.Relative, out var relativeUri) &&
                            relativeUri.IsAbsoluteUri == false &&
                            Uri.TryCreate(mediaUri, snapshotUri, out var resolved))
                        {
                            snapshotUri = resolved.ToString();
                        }

                        result.Success = true;
                        result.Source = snapshotUri;
                        return result;
                    }
                    catch
                    {
                        // Continue trying other tokens/endpoints.
                    }
                }
            }

            string diagnosticsSuffix = BuildOnvifDiagnosticsSuffix(attemptedEndpoints, allTokens, endpointDiagnostics);
            if (snapshotActionNotSupported)
            {
                result.ErrorMessage = $"ONVIF GetSnapshotUri is not supported by this camera. Use ffmpeg RTSP capture for this device.{diagnosticsSuffix}";
                return result;
            }

            result.ErrorMessage = attemptedEndpoints == 0
                ? $"Unable to resolve ONVIF media service URL.{diagnosticsSuffix}"
                : $"Unable to resolve ONVIF snapshot URI after trying {attemptedEndpoints} media endpoint(s).{diagnosticsSuffix}";
            return result;
        }

        private static List<Uri> BuildOnvifMediaServiceUris(string address, int? preferredOnvifPort)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var addressUri))
            {
                addressUri = new Uri($"http://{address.Trim('/')}");
            }
            else if (string.Equals(addressUri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(addressUri)
                {
                    Scheme = Uri.UriSchemeHttp,
                    Port = addressUri.IsDefaultPort ? -1 : addressUri.Port
                };
                addressUri = builder.Uri;
            }

            if (preferredOnvifPort.HasValue)
            {
                var builder = new UriBuilder(addressUri)
                {
                    Scheme = string.Equals(addressUri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                        ? Uri.UriSchemeHttps
                        : Uri.UriSchemeHttp,
                    Port = preferredOnvifPort.Value
                };
                addressUri = builder.Uri;
            }

            if (addressUri.AbsolutePath.Contains("media_service", StringComparison.OrdinalIgnoreCase) ||
                addressUri.AbsolutePath.Contains("media2_service", StringComparison.OrdinalIgnoreCase))
            {
                return new List<Uri> { addressUri };
            }

            return new List<Uri>
            {
                new Uri(addressUri, "/onvif/media_service"),
                new Uri(addressUri, "/onvif/media2_service"),
                new Uri(addressUri, "/onvif/Media"),
                new Uri(addressUri, "/onvif/media")
            };
        }

        private static List<Uri> BuildOnvifDeviceServiceUris(string address, int? preferredOnvifPort)
        {
            var candidates = new List<Uri>();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var addressUri))
            {
                addressUri = new Uri($"http://{address.Trim('/')}");
            }

            // Prioritize Tapo-style ONVIF endpoint first.
            var tapoPreferred = new UriBuilder(addressUri)
            {
                Scheme = Uri.UriSchemeHttp,
                Port = 2020,
                Path = "/"
            };
            candidates.Add(new Uri(tapoPreferred.Uri, "/onvif/device_service"));
            if (preferredOnvifPort.HasValue)
            {
                var preferredBuilder = new UriBuilder(addressUri)
                {
                    Scheme = Uri.UriSchemeHttp,
                    Port = preferredOnvifPort.Value,
                    Path = "/"
                };
                candidates.Add(new Uri(preferredBuilder.Uri, "/onvif/device_service"));
            }

            var schemes = new List<string>();
            if (string.Equals(addressUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                schemes.Add(Uri.UriSchemeHttps);
                schemes.Add(Uri.UriSchemeHttp);
            }
            else
            {
                schemes.Add(Uri.UriSchemeHttp);
                schemes.Add(Uri.UriSchemeHttps);
            }

            var ports = new List<int?> { 2020, null, 80, 443, 8080, 8899 };
            if (preferredOnvifPort.HasValue)
            {
                ports.Insert(0, preferredOnvifPort.Value);
            }
            if (!addressUri.IsDefaultPort)
            {
                ports.Insert(0, addressUri.Port);
            }

            foreach (var scheme in schemes)
            {
                foreach (var port in ports)
                {
                    var builder = new UriBuilder(addressUri)
                    {
                        Scheme = scheme,
                        Port = port ?? -1,
                        Path = "/"
                    };

                    candidates.Add(new Uri(builder.Uri, "/onvif/device_service"));
                    candidates.Add(new Uri(builder.Uri, "/onvif/deviceservice"));
                    candidates.Add(new Uri(builder.Uri, "/device_service"));
                }
            }

            return candidates
                .GroupBy(u => u.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static async Task<List<Uri>> DiscoverOnvifMediaServiceUrisAsync(
            HttpClient client,
            string address,
            int? preferredOnvifPort,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var discovered = new List<Uri>();
            var defaults = BuildOnvifMediaServiceUris(address, preferredOnvifPort);
            discovered.AddRange(defaults);

            var deviceUris = BuildOnvifDeviceServiceUris(address, preferredOnvifPort);
            foreach (var deviceUri in deviceUris)
            {
                try
                {
                    var xAddrs = await DiscoverOnvifMediaXAddrsAsync(client, deviceUri, username, password, cancellationToken);
                    foreach (var xAddr in xAddrs)
                    {
                        if (Uri.TryCreate(xAddr, UriKind.Absolute, out var mediaUri))
                        {
                            discovered.Add(mediaUri);
                        }
                    }
                }
                catch
                {
                    // Keep trying alternate device_service endpoints.
                }
            }

            return discovered
                .GroupBy(u => u.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static string ExtractSnapshotUri(string xml)
        {
            try
            {
                var document = XDocument.Parse(xml);
                return document
                    .Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Uri", StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static List<string> ExtractOnvifMediaXAddrs(string xml)
        {
            try
            {
                var document = XDocument.Parse(xml);
                return document
                    .Descendants()
                    .Where(e => string.Equals(e.Name.LocalName, "XAddr", StringComparison.OrdinalIgnoreCase))
                    .Where(e => e.Ancestors().Any(a => a.Name.LocalName.Contains("Media", StringComparison.OrdinalIgnoreCase)))
                    .Select(e => e.Value?.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static string BuildGetCapabilitiesOperationXml()
        {
            return
                @"<GetCapabilities xmlns=""http://www.onvif.org/ver10/device/wsdl"">
  <Category>All</Category>
</GetCapabilities>";
        }

        private static string BuildGetMediaServiceCapabilitiesOperationXml()
        {
            return @"<GetServiceCapabilities xmlns=""http://www.onvif.org/ver10/media/wsdl"" />";
        }

        private static async Task<List<string>> DiscoverOnvifMediaXAddrsAsync(
            HttpClient client,
            Uri deviceServiceUri,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            string operationXml = BuildGetCapabilitiesOperationXml();
            var response = await PostOnvifSoapAsync(client, deviceServiceUri, operationXml, username, password, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<string>();
            }

            return ExtractOnvifMediaXAddrs(response.Xml);
        }

        private static string BuildGetSnapshotUriOperationXml(string profileToken)
        {
            return
                $@"<GetSnapshotUri xmlns=""http://www.onvif.org/ver10/media/wsdl"">
  <ProfileToken>{SecurityElementEscape(profileToken)}</ProfileToken>
</GetSnapshotUri>";
        }

        private static string BuildGetProfilesOperationXml()
        {
            return
                @"<GetProfiles xmlns=""http://www.onvif.org/ver10/media/wsdl"" />";
        }

        private sealed class OnvifMediaCapabilities
        {
            public bool? SnapshotUri { get; set; }
            public bool? NoRtspStreaming { get; set; }
            public bool? RtpRtspTcp { get; set; }
            public bool? RtpTcp { get; set; }
            public bool? RtpMulticast { get; set; }
            public int? MaximumNumberOfProfiles { get; set; }
        }

        private sealed class OnvifMediaCapabilitiesResult
        {
            public OnvifSoapResponse Response { get; set; } = new OnvifSoapResponse();
            public OnvifMediaCapabilities? Capabilities { get; set; }
        }

        private sealed class OnvifProfileDiscoveryResult
        {
            public List<string> Tokens { get; set; } = new List<string>();
        }

        private static OnvifMediaCapabilities? ExtractOnvifMediaCapabilities(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var capabilitiesElement = doc
                    .Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Capabilities", StringComparison.OrdinalIgnoreCase));
                if (capabilitiesElement == null)
                {
                    return null;
                }

                int? maxProfiles = null;
                var profileCaps = capabilitiesElement
                    .Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "ProfileCapabilities", StringComparison.OrdinalIgnoreCase));
                if (profileCaps != null)
                {
                    var maxProfilesAttr = profileCaps.Attributes()
                        .FirstOrDefault(a => string.Equals(a.Name.LocalName, "MaximumNumberOfProfiles", StringComparison.OrdinalIgnoreCase))
                        ?.Value;
                    if (int.TryParse(maxProfilesAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMaxProfiles))
                    {
                        maxProfiles = parsedMaxProfiles;
                    }
                }

                var streamingCaps = capabilitiesElement
                    .Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "StreamingCapabilities", StringComparison.OrdinalIgnoreCase));

                return new OnvifMediaCapabilities
                {
                    SnapshotUri = ParseBoolAttribute(capabilitiesElement, "SnapshotUri"),
                    NoRtspStreaming = ParseBoolAttribute(streamingCaps, "NoRTSPStreaming"),
                    RtpRtspTcp = ParseBoolAttribute(streamingCaps, "RTP_RTSP_TCP"),
                    RtpTcp = ParseBoolAttribute(streamingCaps, "RTP_TCP"),
                    RtpMulticast = ParseBoolAttribute(streamingCaps, "RTPMulticast"),
                    MaximumNumberOfProfiles = maxProfiles
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool? ParseBoolAttribute(XElement? element, string name)
        {
            if (element == null)
            {
                return null;
            }

            var value = element.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static async Task<OnvifMediaCapabilitiesResult> DiscoverOnvifMediaServiceCapabilitiesAsync(
            HttpClient client,
            Uri mediaUri,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            string operationXml = BuildGetMediaServiceCapabilitiesOperationXml();
            var response = await PostOnvifSoapAsync(client, mediaUri, operationXml, username, password, cancellationToken);
            return new OnvifMediaCapabilitiesResult
            {
                Response = response,
                Capabilities = response.IsSuccessStatusCode ? ExtractOnvifMediaCapabilities(response.Xml) : null
            };
        }

        private static List<string> ExtractOnvifProfileTokens(string xml)
        {
            try
            {
                var document = XDocument.Parse(xml);
                return document
                    .Descendants()
                    .Where(e => string.Equals(e.Name.LocalName, "Profiles", StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "token", StringComparison.OrdinalIgnoreCase))?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.Ordinal)
                    .Cast<string>()
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static async Task<OnvifProfileDiscoveryResult> DiscoverOnvifProfileTokensAsync(
            HttpClient client,
            Uri mediaUri,
            string preferredProfileToken,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var result = new OnvifProfileDiscoveryResult();
            if (!string.IsNullOrWhiteSpace(preferredProfileToken))
            {
                result.Tokens.Add(preferredProfileToken);
            }

            string operationXml = BuildGetProfilesOperationXml();
            var response = await PostOnvifSoapAsync(client, mediaUri, operationXml, username, password, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return result;
            }

            var discovered = ExtractOnvifProfileTokens(response.Xml);
            foreach (var token in discovered)
            {
                if (result.Tokens.Contains(token, StringComparer.Ordinal))
                {
                    continue;
                }

                result.Tokens.Add(token);
            }

            return result;
        }

        private sealed class OnvifSoapResponse
        {
            public bool IsSuccessStatusCode { get; set; }
            public string Xml { get; set; } = string.Empty;
            public HttpStatusCode StatusCode { get; set; }
            public bool UsedWsseDigest { get; set; }
        }

        private static async Task<OnvifSoapResponse> PostOnvifSoapAsync(
            HttpClient client,
            Uri endpoint,
            string operationXml,
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            var plainEnvelope = BuildSoapEnvelope(operationXml, wsseHeaderXml: null);
            using var plainContent = new StringContent(plainEnvelope, Encoding.UTF8, "application/soap+xml");
            using var plainResponse = await client.PostAsync(endpoint, plainContent, cancellationToken);
            var plainXml = await plainResponse.Content.ReadAsStringAsync(cancellationToken);
            if (plainResponse.IsSuccessStatusCode)
            {
                return new OnvifSoapResponse
                {
                    IsSuccessStatusCode = true,
                    Xml = plainXml,
                    StatusCode = plainResponse.StatusCode,
                    UsedWsseDigest = false
                };
            }

            if (string.IsNullOrWhiteSpace(username) ||
                !(plainResponse.StatusCode == HttpStatusCode.Unauthorized ||
                  plainResponse.StatusCode == HttpStatusCode.Forbidden ||
                  IsOnvifNotAuthorizedFault(plainXml)))
            {
                return new OnvifSoapResponse
                {
                    IsSuccessStatusCode = false,
                    Xml = plainXml,
                    StatusCode = plainResponse.StatusCode,
                    UsedWsseDigest = false
                };
            }

            var wsseHeader = BuildWsseSecurityHeaderXml(username, password ?? string.Empty);
            var wsseEnvelope = BuildSoapEnvelope(operationXml, wsseHeader);
            using var wsseContent = new StringContent(wsseEnvelope, Encoding.UTF8, "application/soap+xml");
            using var wsseResponse = await client.PostAsync(endpoint, wsseContent, cancellationToken);
            var wsseXml = await wsseResponse.Content.ReadAsStringAsync(cancellationToken);
            return new OnvifSoapResponse
            {
                IsSuccessStatusCode = wsseResponse.IsSuccessStatusCode,
                Xml = wsseXml,
                StatusCode = wsseResponse.StatusCode,
                UsedWsseDigest = true
            };
        }

        private static bool IsOnvifNotAuthorizedFault(string xml)
        {
            return xml.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOnvifActionNotSupportedFault(string xml)
        {
            return xml.Contains("ActionNotSupported", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetOnvifFaultKind(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return "unknown";
            }

            if (IsOnvifActionNotSupportedFault(xml))
            {
                return "ActionNotSupported";
            }

            if (IsOnvifNotAuthorizedFault(xml))
            {
                return "NotAuthorized";
            }

            if (xml.Contains("Sender", StringComparison.OrdinalIgnoreCase))
            {
                return "SenderFault";
            }

            if (xml.Contains("Receiver", StringComparison.OrdinalIgnoreCase))
            {
                return "ReceiverFault";
            }

            return "unknown";
        }

        private static string FormatAuthMode(OnvifSoapResponse response)
        {
            return response.UsedWsseDigest ? "wsse_digest" : "none_or_http_auth";
        }

        private static string FormatBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
        }

        private static string FormatInt(int? value)
        {
            return value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        }

        private static string BuildOnvifDiagnosticsSuffix(
            int attemptedEndpoints,
            HashSet<string> discoveredTokens,
            List<string> endpointDiagnostics)
        {
            var sb = new StringBuilder();
            sb.Append(" ONVIF diagnostics: ");
            sb.Append($"attempted_endpoints={attemptedEndpoints}");
            if (discoveredTokens.Count > 0)
            {
                sb.Append($"; discovered_tokens=[{string.Join(",", discoveredTokens)}]");
            }

            if (endpointDiagnostics.Count > 0)
            {
                sb.Append("; endpoint_details=");
                sb.Append(string.Join(" | ", endpointDiagnostics.Take(12)));
                if (endpointDiagnostics.Count > 12)
                {
                    sb.Append($" | ...(+{endpointDiagnostics.Count - 12} more)");
                }
            }

            return sb.ToString();
        }

        private static string BuildSoapEnvelope(string operationXml, string? wsseHeaderXml)
        {
            var header = string.IsNullOrWhiteSpace(wsseHeaderXml)
                ? string.Empty
                : $"<s:Header>{wsseHeaderXml}</s:Header>";

            return
                $@"<?xml version=""1.0"" encoding=""utf-8""?>
<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope""
            xmlns:wsse=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd""
            xmlns:wsu=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"">
  {header}
  <s:Body>
    {operationXml}
  </s:Body>
</s:Envelope>";
        }

        private static string BuildWsseSecurityHeaderXml(string username, string password)
        {
            Span<byte> nonceBytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(nonceBytes);
            var nonce = Convert.ToBase64String(nonceBytes);
            var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var digestBytes = SHA1.HashData(CombineBytes(
                nonceBytes.ToArray(),
                Encoding.UTF8.GetBytes(created),
                Encoding.UTF8.GetBytes(password)));
            var passwordDigest = Convert.ToBase64String(digestBytes);

            return
                $@"<wsse:Security s:mustUnderstand=""1"">
  <wsse:UsernameToken>
    <wsse:Username>{SecurityElementEscape(username)}</wsse:Username>
    <wsse:Password Type=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"">{passwordDigest}</wsse:Password>
    <wsse:Nonce EncodingType=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"">{nonce}</wsse:Nonce>
    <wsu:Created>{created}</wsu:Created>
  </wsse:UsernameToken>
</wsse:Security>";
        }

        private static byte[] CombineBytes(params byte[][] values)
        {
            int totalLength = values.Sum(v => v.Length);
            var combined = new byte[totalLength];
            int offset = 0;
            foreach (var value in values)
            {
                Buffer.BlockCopy(value, 0, combined, offset, value.Length);
                offset += value.Length;
            }

            return combined;
        }

        private async Task<CaptureResult> CaptureStillWithFfmpegAsync(
            string sourceUrl,
            string username,
            string password,
            string ffmpegPath,
            int longEdge,
            CancellationToken cancellationToken,
            bool isRtsp,
            int? rtspPort)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"camera-capture-{Guid.NewGuid():N}.jpg");
            try
            {
                if (Path.IsPathRooted(ffmpegPath) && !File.Exists(ffmpegPath))
                {
                    string guidance = string.Empty;
                    if (!string.IsNullOrWhiteSpace(_netConfig.NativeLibDir))
                    {
                        guidance = " On Android, deploy libffmpeg_exec.so into Platforms/Android/jniLibs/<abi>/ and rebuild the app.";
                    }

                    return new CaptureResult
                    {
                        Success = false,
                        ErrorMessage = $"ffmpeg executable not found at '{ffmpegPath}'.{guidance}".Trim()
                    };
                }

                if (isRtsp)
                {
                    sourceUrl = BuildRtspUrl(sourceUrl, username, password, string.Empty, rtspPort);
                }

                var args = new List<string>
                {
                    "-y"
                };

                if (isRtsp)
                {
                    args.Add("-rtsp_transport");
                    args.Add("tcp");
                }

                args.Add("-i");
                args.Add(sourceUrl);
                args.Add("-frames:v");
                args.Add("1");
                args.Add("-vf");
                args.Add($"scale='if(gt(iw,ih),{longEdge},-2)':'if(gt(iw,ih),-2,{longEdge})'");
                args.Add("-q:v");
                args.Add(JpegQuality.ToString(CultureInfo.InvariantCulture));
                args.Add(tempFile);

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (!string.IsNullOrWhiteSpace(_netConfig.CommandPath) && Directory.Exists(_netConfig.CommandPath))
                {
                    psi.WorkingDirectory = _netConfig.CommandPath;
                }

                foreach (var arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }

                using var process = new Process { StartInfo = psi };
                process.Start();
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0 || !File.Exists(tempFile))
                {
                    return new CaptureResult
                    {
                        Success = false,
                        ErrorMessage = $"ffmpeg failed to capture frame. {stderr}".Trim()
                    };
                }

                var bytes = await File.ReadAllBytesAsync(tempFile, cancellationToken);
                return new CaptureResult
                {
                    Success = true,
                    ImageBytes = bytes,
                    Source = sourceUrl
                };
            }
            catch (Exception ex)
            {
                return new CaptureResult
                {
                    Success = false,
                    ErrorMessage = $"Camera capture process failed: {ex.Message}"
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                }
            }
        }

        private async Task<CaptureResult> CaptureStillFromHttpAsync(
            string sourceUrl,
            string username,
            string password,
            bool allowInsecureTls,
            CancellationToken cancellationToken)
        {
            try
            {
                var handler = CreateCameraHttpClientHandler(username, password, allowInsecureTls);

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(20)
                };
                using var response = await client.GetAsync(sourceUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new CaptureResult
                    {
                        Success = false,
                        ErrorMessage = $"HTTP snapshot request failed with status {(int)response.StatusCode}."
                    };
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length == 0)
                {
                    return new CaptureResult
                    {
                        Success = false,
                        ErrorMessage = "HTTP snapshot request returned empty content."
                    };
                }

                return new CaptureResult
                {
                    Success = true,
                    ImageBytes = bytes,
                    Source = sourceUrl
                };
            }
            catch (Exception ex)
            {
                return new CaptureResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP snapshot fallback failed: {ex.Message}"
                };
            }
        }

        private static HttpClientHandler CreateCameraHttpClientHandler(
            string username,
            string password,
            bool allowInsecureTls)
        {
            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(username))
            {
                handler.Credentials = new NetworkCredential(username, password ?? string.Empty);
                handler.PreAuthenticate = true;
            }

            if (allowInsecureTls)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            return handler;
        }

        private static string BuildRtspUrl(string address, string username, string password, string rtspPath, int? rtspPort)
        {
            var source = address.Trim();
            if (!source.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
            {
                source = source.Trim('/');
                source = "rtsp://" + source;
                if (!string.IsNullOrWhiteSpace(rtspPath))
                {
                    source = source.TrimEnd('/') + "/" + rtspPath.TrimStart('/');
                }
            }

            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                return source;
            }

            var builder = new UriBuilder(uri);
            if (rtspPort.HasValue)
            {
                builder.Port = rtspPort.Value;
            }

            if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                builder.UserName = username;
                builder.Password = password ?? string.Empty;
            }

            return builder.Uri.ToString();
        }

        private static string SecurityElementEscape(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);
        }
    }
}
