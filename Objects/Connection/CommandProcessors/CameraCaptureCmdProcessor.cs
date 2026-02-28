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
            string ffmpegPath = GetArg(args, "ffmpeg_path", "ffmpeg");
            string profileToken = GetArg(args, "profile_token", "Profile_1");
            string rtspPath = GetArg(args, "rtsp_path", "");
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
                    profileToken,
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
- --ffmpeg_path <path> (default: ffmpeg)
- --allow_insecure_tls true|false (default: true; for ONVIF HTTPS/self-signed certs)

Examples:
- --protocol rtsp --address 192.168.1.10 --username admin --password pass
- --protocol onvif --address 192.168.1.20 --username admin --password pass --profile_token Profile_1
";
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
            string profileToken,
            string ffmpegPath,
            int longEdge,
            bool allowInsecureTls,
            CancellationToken cancellationToken)
        {
            if (string.Equals(protocol, "onvif", StringComparison.OrdinalIgnoreCase))
            {
                var snapshotResult = await ResolveOnvifSnapshotUriAsync(address, username, password, profileToken, allowInsecureTls, cancellationToken);
                if (!snapshotResult.Success || string.IsNullOrWhiteSpace(snapshotResult.Source))
                {
                    return snapshotResult;
                }

                var ffmpegResult = await CaptureStillWithFfmpegAsync(snapshotResult.Source, username, password, ffmpegPath, longEdge, cancellationToken, isRtsp: false);
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

            var rtspUrl = BuildRtspUrl(address, username, password, rtspPath);
            var ffmpegRtspResult = await CaptureStillWithFfmpegAsync(rtspUrl, username, password, ffmpegPath, longEdge, cancellationToken, isRtsp: true);
            if (ffmpegRtspResult.Success)
            {
                return ffmpegRtspResult;
            }

            var snapshotFallback = await ResolveOnvifSnapshotUriAsync(address, username, password, profileToken, allowInsecureTls, cancellationToken);
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
            bool allowInsecureTls,
            CancellationToken cancellationToken)
        {
            var result = new CaptureResult { Success = false };

            var handler = CreateCameraHttpClientHandler(username, password, allowInsecureTls);

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            var candidateUris = await DiscoverOnvifMediaServiceUrisAsync(client, address, cancellationToken);
            string[] tokenCandidates = new[] { profileToken };
            int attemptedEndpoints = 0;

            foreach (var mediaUri in candidateUris)
            {
                attemptedEndpoints++;
                try
                {
                    var discoveredTokens = await DiscoverOnvifProfileTokensAsync(client, mediaUri, profileToken, cancellationToken);
                    if (discoveredTokens.Count > 0)
                    {
                        tokenCandidates = discoveredTokens.ToArray();
                    }
                }
                catch
                {
                }

                foreach (var token in tokenCandidates)
                {
                    try
                    {
                        string body = BuildGetSnapshotUriBody(token);
                        using var content = new StringContent(body, Encoding.UTF8, "application/soap+xml");
                        using var response = await client.PostAsync(mediaUri, content, cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            continue;
                        }

                        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                        var snapshotUri = ExtractSnapshotUri(xml);
                        if (string.IsNullOrWhiteSpace(snapshotUri))
                        {
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

            result.ErrorMessage = attemptedEndpoints == 0
                ? "Unable to resolve ONVIF media service URL."
                : $"Unable to resolve ONVIF snapshot URI after trying {attemptedEndpoints} media endpoint(s).";
            return result;
        }

        private static List<Uri> BuildOnvifMediaServiceUris(string address)
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

        private static List<Uri> BuildOnvifDeviceServiceUris(string address)
        {
            var candidates = new List<Uri>();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var addressUri))
            {
                addressUri = new Uri($"http://{address.Trim('/')}");
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

            var ports = new List<int?> { null, 80, 443, 8080, 8899 };
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
            CancellationToken cancellationToken)
        {
            var discovered = new List<Uri>();
            var defaults = BuildOnvifMediaServiceUris(address);
            discovered.AddRange(defaults);

            var deviceUris = BuildOnvifDeviceServiceUris(address);
            foreach (var deviceUri in deviceUris)
            {
                try
                {
                    var xAddrs = await DiscoverOnvifMediaXAddrsAsync(client, deviceUri, cancellationToken);
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

        private static string BuildGetCapabilitiesBody()
        {
            return
                @"<?xml version=""1.0"" encoding=""utf-8""?>
<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"">
  <s:Body>
    <GetCapabilities xmlns=""http://www.onvif.org/ver10/device/wsdl"">
      <Category>All</Category>
    </GetCapabilities>
  </s:Body>
</s:Envelope>";
        }

        private static async Task<List<string>> DiscoverOnvifMediaXAddrsAsync(
            HttpClient client,
            Uri deviceServiceUri,
            CancellationToken cancellationToken)
        {
            string body = BuildGetCapabilitiesBody();
            using var content = new StringContent(body, Encoding.UTF8, "application/soap+xml");
            using var response = await client.PostAsync(deviceServiceUri, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<string>();
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractOnvifMediaXAddrs(xml);
        }

        private static string BuildGetSnapshotUriBody(string profileToken)
        {
            return
                $@"<?xml version=""1.0"" encoding=""utf-8""?>
<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"">
  <s:Body>
    <GetSnapshotUri xmlns=""http://www.onvif.org/ver10/media/wsdl"">
      <ProfileToken>{SecurityElementEscape(profileToken)}</ProfileToken>
    </GetSnapshotUri>
  </s:Body>
</s:Envelope>";
        }

        private static string BuildGetProfilesBody()
        {
            return
                @"<?xml version=""1.0"" encoding=""utf-8""?>
<s:Envelope xmlns:s=""http://www.w3.org/2003/05/soap-envelope"">
  <s:Body>
    <GetProfiles xmlns=""http://www.onvif.org/ver10/media/wsdl"" />
  </s:Body>
</s:Envelope>";
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

        private static async Task<List<string>> DiscoverOnvifProfileTokensAsync(
            HttpClient client,
            Uri mediaUri,
            string preferredProfileToken,
            CancellationToken cancellationToken)
        {
            var tokens = new List<string>();
            if (!string.IsNullOrWhiteSpace(preferredProfileToken))
            {
                tokens.Add(preferredProfileToken);
            }

            string body = BuildGetProfilesBody();
            using var content = new StringContent(body, Encoding.UTF8, "application/soap+xml");
            using var response = await client.PostAsync(mediaUri, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return tokens;
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var discovered = ExtractOnvifProfileTokens(xml);
            foreach (var token in discovered)
            {
                if (tokens.Contains(token, StringComparer.Ordinal))
                {
                    continue;
                }

                tokens.Add(token);
            }

            return tokens;
        }

        private async Task<CaptureResult> CaptureStillWithFfmpegAsync(
            string sourceUrl,
            string username,
            string password,
            string ffmpegPath,
            int longEdge,
            CancellationToken cancellationToken,
            bool isRtsp)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"camera-capture-{Guid.NewGuid():N}.jpg");
            try
            {
                if (isRtsp)
                {
                    sourceUrl = BuildRtspUrl(sourceUrl, username, password, string.Empty);
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

        private static string BuildRtspUrl(string address, string username, string password, string rtspPath)
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

            if (string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                return source;
            }

            var builder = new UriBuilder(uri)
            {
                UserName = username,
                Password = password ?? string.Empty
            };
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
