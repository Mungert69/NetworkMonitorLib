using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Objects.Repository.Helpers
{
    /// <summary>
    /// Android 5/6 (SDK &lt;= 23) certificate validation:
    /// - Anchor to ISRG Root X1 (embedded or file).
    /// - Ensure Let's Encrypt chain.pem intermediate is available:
    ///   * Prefer app-configured URL (served by your nginx),
    ///   * else embedded copy,
    ///   * plus any intermediates surfaced by the platform.
    /// </summary>
    internal static class LegacyAndroidSslHelper
    {
        private const int LegacyAndroidMaxSdkLevel = 23;

        private static X509Certificate2? _cachedIsrgRoot;
        private static X509Certificate2? _cachedLetsEncryptChainEmbedded;
        private static readonly Dictionary<string, X509Certificate2> DownloadedIntermediates = new(StringComparer.OrdinalIgnoreCase);

        internal static void Configure(SystemUrl systemUrl, SslOption sslOption, ILogger? logger)
        {
            if (sslOption == null || systemUrl == null || !sslOption.Enabled) return;
            if (systemUrl.AndroidSdkLevel <= 0 || systemUrl.AndroidSdkLevel > LegacyAndroidMaxSdkLevel) return;

            logger?.LogDebug("Applying legacy Android certificate handling for SDK level {SdkLevel}.", systemUrl.AndroidSdkLevel);

            if (string.IsNullOrWhiteSpace(sslOption.ServerName))
                sslOption.ServerName = systemUrl.RabbitHostName; // SNI + hostname check

            sslOption.AcceptablePolicyErrors = SslPolicyErrors.None;
            sslOption.Version = SslProtocols.Tls12;

            sslOption.CertificateValidationCallback = (sender, certificate, chain, errors) =>
                ValidateWithCustomTrustStore(certificate, chain, errors, logger, systemUrl);
        }

        private static bool ValidateWithCustomTrustStore(
            X509Certificate? certificate,
            X509Chain? platformChain,
            SslPolicyErrors errors,
            ILogger? logger,
            SystemUrl systemUrl)
        {
            if (certificate == null)
            {
                logger?.LogWarning("Certificate validation failed: no certificate was provided.");
                return false;
            }

            if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                logger?.LogWarning("Certificate name mismatch.");
                return false;
            }

            if (errors == SslPolicyErrors.None) return true; // rare on 5/6

            X509Certificate2? leaf = null;
            var disposeLeaf = false;

            try
            {
                leaf = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
                disposeLeaf = !(certificate is X509Certificate2);

                var root = GetRootCertificate(systemUrl, logger);

                // Build a list of intermediates/extras we will offer the chain engine.
                var extras = new List<X509Certificate2>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void Add(X509Certificate2? c)
                {
                    if (c == null) return;
                    var t = c.Thumbprint;
                    if (string.IsNullOrEmpty(t)) return;
                    if (seen.Add(t)) extras.Add(c);
                }

                // Root in ExtraStore helps path building on legacy.
                Add(root);

                // From platform callback (often empty on 5/6)
                var platformSubjects = new List<string>();
                if (platformChain != null)
                {
                    try
                    {
                        platformChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        platformChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                        platformChain.Build(leaf);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Failed to build platform chain before extracting intermediates.");
                    }

                    foreach (var e in platformChain.ChainElements)
                    {
                        var c = e.Certificate;
                        if (c == null) continue;
                        platformSubjects.Add(c.Subject);
                        if (!string.Equals(c.Thumbprint, leaf.Thumbprint, StringComparison.OrdinalIgnoreCase))
                            Add(c);
                    }

                    logger?.LogDebug("Platform chain elems: {Elems}",
                        platformSubjects.Count == 0 ? "<empty>" : string.Join(" -> ", platformSubjects));
                }

                // Try to fetch intermediates from configured path(s) – optional.
                foreach (var url in EnumerateIntermediateUrls(systemUrl))
                {
                    Add(TryFetchIntermediateFromUrl(url, logger));
                }

                // Always add embedded fallback.
                Add(GetEmbeddedLetsEncryptChain(logger));

                // Log extras we will provide
                logger?.LogDebug("ExtraStore (subjects): {Subs}",
                    extras.Count == 0 ? "<empty>" : string.Join(" | ", extras.Select(c => c.Subject)));

                // Attempt 1: CustomRootTrust anchored at our ISRG root
                if (TryBuildChain(leaf, root, extras, useCustomRootTrust: true, logWarnings: false, logger, "custom-root"))
                    return true;

                // Attempt 2: System trust (in case the platform is okay once extras are present)
                if (TryBuildChain(leaf, root, extras, useCustomRootTrust: false, logWarnings: true, logger, "system-trust"))
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Certificate validation threw an exception for legacy Android.");
                return false;
            }
            finally
            {
                if (disposeLeaf && leaf != null) leaf.Dispose();
            }
        }

        private static bool TryBuildChain(
            X509Certificate2 leaf,
            X509Certificate2 rootCert,
            IReadOnlyCollection<X509Certificate2> extraStore,
            bool useCustomRootTrust,
            bool logWarnings,
            ILogger? logger,
            string attemptLabel)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.CustomTrustStore.Clear();
            chain.ChainPolicy.ExtraStore.Clear();

            foreach (var cert in extraStore)
                chain.ChainPolicy.ExtraStore.Add(cert);

            if (useCustomRootTrust)
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(rootCert);
            }
            else
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.System;
            }

            var ok = chain.Build(leaf);

            var builtSubjects = chain.ChainElements.Cast<X509ChainElement>().Select(e => e.Certificate.Subject).ToList();
            logger?.LogDebug("{AttemptLabel} chain elems: {Elems}",
                attemptLabel, builtSubjects.Count == 0 ? "<empty>" : string.Join(" -> ", builtSubjects));

            if (ok) return true;

            foreach (var status in chain.ChainStatus)
            {
                if (logWarnings)
                    logger?.LogWarning("Certificate chain validation failed ({Attempt}): {Status} - {Info}", attemptLabel, status.Status, status.StatusInformation);
                else
                    logger?.LogDebug("Certificate chain validation failed ({Attempt}): {Status} - {Info}", attemptLabel, status.Status, status.StatusInformation);
            }

            // Accept if we anchored at our ISRG root and only saw benign flags
            var anchored = chain.ChainElements.Cast<X509ChainElement>()
                .Any(e => string.Equals(e.Certificate.Thumbprint, rootCert.Thumbprint, StringComparison.OrdinalIgnoreCase));

            bool onlyBenign = chain.ChainStatus.All(s =>
                s.Status == X509ChainStatusFlags.UntrustedRoot ||
                s.Status == X509ChainStatusFlags.PartialChain ||
                s.Status == X509ChainStatusFlags.RevocationStatusUnknown);

            if (anchored && onlyBenign)
            {
                logger?.LogDebug("{Attempt} chain anchored at embedded root; accepting despite statuses {Statuses}.",
                    attemptLabel, string.Join(", ", chain.ChainStatus.Select(s => s.Status)));
                return true;
            }

            return false;
        }

        private static X509Certificate2 GetRootCertificate(SystemUrl systemUrl, ILogger? logger)
        {
            if (_cachedIsrgRoot != null) return _cachedIsrgRoot;

            var path = systemUrl.LegacyAndroidRootCertPath; // optional file on device
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        _cachedIsrgRoot = X509CertificateLoader.LoadCertificateFromFile(path);
                        return _cachedIsrgRoot;
                    }
                    logger?.LogWarning("Legacy Android root certificate was not found at {Path}. Falling back to embedded copy.", path);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to load legacy Android root certificate from {Path}. Falling back to embedded copy.", path);
                }
            }

            _cachedIsrgRoot = X509Certificate2.CreateFromPem(IsrgRootX1Pem);
            return _cachedIsrgRoot;
        }

        private static X509Certificate2? GetEmbeddedLetsEncryptChain(ILogger? logger)
        {
            if (_cachedLetsEncryptChainEmbedded != null) return _cachedLetsEncryptChainEmbedded;
            try
            {
                _cachedLetsEncryptChainEmbedded = X509Certificate2.CreateFromPem(LetsEncryptChainPem);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to parse embedded Let's Encrypt chain.pem intermediate certificate.");
            }
            return _cachedLetsEncryptChainEmbedded;
        }

        private static X509Certificate2? TryFetchIntermediateFromUrl(string? url, ILogger? logger)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (DownloadedIntermediates.TryGetValue(url, out var cached)) return cached;

            try
            {
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = static (_, __, ___, ____) => true
                };
                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(5)
                };

                var pem = client.GetStringAsync(url).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(pem)) return null;

                var cert = X509Certificate2.CreateFromPem(pem);
                DownloadedIntermediates[url] = cert;
                logger?.LogDebug("Downloaded intermediate from {Url}: {Subject}", url, cert.Subject);
                return cert;
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Failed to download intermediate from {Url}. Will rely on embedded copy / platform extras.", url);
                return null;
            }
        }

        private static IEnumerable<string> EnumerateIntermediateUrls(SystemUrl systemUrl)
        {
            if (string.IsNullOrWhiteSpace(systemUrl.LegacyIntermediateUrl))
                yield break;

            var trimmed = systemUrl.LegacyIntermediateUrl.TrimEnd('/');

            if (trimmed.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
            {
                yield return trimmed;
                yield break;
            }

            yield return $"{trimmed}/chain.pem";
        }

        private const string IsrgRootX1Pem = @"-----BEGIN CERTIFICATE-----
MIIFazCCA1OgAwIBAgIRAIIQz7DSQONZRGPgu2OCiwAwDQYJKoZIhvcNAQELBQAw
TzELMAkGA1UEBhMCVVMxKTAnBgNVBAoTIEludGVybmV0IFNlY3VyaXR5IFJlc2Vh
cmNoIEdyb3VwMRUwEwYDVQQDEwxJU1JHIFJvb3QgWDEwHhcNMTUwNjA0MTEwNDM4
WhcNMzUwNjA0MTEwNDM4WjBPMQswCQYDVQQGEwJVUzEpMCcGA1UEChMgSW50ZXJu
ZXQgU2VjdXJpdHkgUmVzZWFyY2ggR3JvdXAxFTATBgNVBAMTDElTUkcgUm9vdCBY
MTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAK3oJHP0FDfzm54rVygc
h77ct984kIxuPOZXoHj3dcKi/vVqbvYATyjb3miGbESTtrFj/RQSa78f0uoxmyF+
0TM8ukj13Xnfs7j/EvEhmkvBioZxaUpmZmyPfjxwv60pIgbz5MDmgK7iS4+3mX6U
A5/TR5d8mUgjU+g4rk8Kb4Mu0UlXjIB0ttov0DiNewNwIRt18jA8+o+u3dpjq+sW
T8KOEUt+zwvo/7V3LvSye0rgTBIlDHCNAymg4VMk7BPZ7hm/ELNKjD+Jo2FR3qyH
B5T0Y3HsLuJvW5iB4YlcNHlsdu87kGJ55tukmi8mxdAQ4Q7e2RCOFvu396j3x+UC
B5iPNgiV5+I3lg02dZ77DnKxHZu8A/lJBdiB3QW0KtZB6awBdpUKD9jf1b0SHzUv
KBds0pjBqAlkd25HN7rOrFleaJ1/ctaJxQZBKT5ZPt0m9STJEadao0xAH0ahmbWn
OlFuhjuefXKnEgV4We0+UXgVCwOPjdAvBbI+e0ocS3MFEvzG6uBQE3xDk3SzynTn
jh8BCNAw1FtxNrQHusEwMFxIt4I7mKZ9YIqioymCzLq9gwQbooMDQaHWBfEbwrbw
qHyGO0aoSCqI3Haadr8faqU9GY/rOPNk3sgrDQoo//fb4hVC1CLQJ13hef4Y53CI
rU7m2Ys6xt0nUW7/vGT1M0NPAgMBAAGjQjBAMA4GA1UdDwEB/wQEAwIBBjAPBgNV
HRMBAf8EBTADAQH/MB0GA1UdDgQWBBR5tFnme7bl5AFzgAiIyBpY9umbbjANBgkq
hkiG9w0BAQsFAAOCAgEAVR9YqbyyqFDQDLHYGmkgJykIrGF1XIpu+ILlaS/V9lZL
ubhzEFnTIZd+50xx+7LSYK05qAvqFyFWhfFQDlnrzuBZ6brJFe+GnY+EgPbk6ZGQ
3BebYhtF8GaV0nxvwuo77x/Py9auJ/GpsMiu/X1+mvoiBOv/2X/qkSsisRcOj/KK
NFtY2PwByVS5uCbMiogziUwthDyC3+6WVwW6LLv3xLfHTjuCvjHIInNzktHCgKQ5
ORAzI4JMPJ+GslWYHb4phowim57iaztXOoJwTdwJx4nLCgdNbOhdjsnvzqvHu7Ur
TkXWStAmzOVyyghqpZXjFaH3pO3JLF+l+/+sKAIuvtd7u+Nxe5AW0wdeRlN8NwdC
jNPElpzVmbUq4JUagEiuTDkHzsxHpFKVK7q4+63SM1N95R1NbdWhscdCb+ZAJzVc
oyi3B43njTOQ5yOf+1CceWxG1bQVs5ZufpsMljq4Ui0/1lvh+wjChP4kqKOJ2qxq
4RgqsahDYVvTH9w7jXbyLeiNdd8XM2w9U/t7y0Ff/9yi0GE44Za4rF2LN9d11TPA
mRGunUHBcnWEvgJBQl9nJEiU0Zsnvgc/ubhPgXRR4Xq37Z0j4r7g1SgEEzwxA57d
emyPxgcYxn/eR44/KJ4EBs+lVDR3veyJm+kXQ99b21/+jh5Xos1AnX5iItreGCc=
-----END CERTIFICATE-----";

        private const string LetsEncryptChainPem = @"-----BEGIN CERTIFICATE-----
MIIEVjCCAj6gAwIBAgIQY5WTY8JOcIJxWRi/w9ftVjANBgkqhkiG9w0BAQsFADBP
MQswCQYDVQQGEwJVUzEpMCcGA1UEChMgSW50ZXJuZXQgU2VjdXJpdHkgUmVzZWFy
Y2ggR3JvdXAxFTATBgNVBAMTDElTUkcgUm9vdCBYMTAeFw0yNDAzMTMwMDAwMDBa
Fw0yNzAzMTIyMzU5NTlaMDIxCzAJBgNVBAYTAlVTMRYwFAYDVQQKEw1MZXQncyBF
bmNyeXB0MQswCQYDVQQDEwJFODB2MBAGByqGSM49AgEGBSuBBAAiA2IABNFl8l7c
S7QMApzSsvru6WyrOq44ofTUOTIzxULUzDMMNMchIJBwXOhiLxxxs0LXeb5GDcHb
R6EToMffgSZjO9SNHfY9gjMy9vQr5/WWOrQTZxh7az6NSNnq3u2ubT6HTKOB+DCB
9TAOBgNVHQ8BAf8EBAMCAYYwHQYDVR0lBBYwFAYIKwYBBQUHAwIGCCsGAQUFBwMB
MBIGA1UdEwEB/wQIMAYBAf8CAQAwHQYDVR0OBBYEFI8NE6L2Ln7RUGwzGDhdWY4j
cpHKMB8GA1UdIwQYMBaAFHm0WeZ7tuXkAXOACIjIGlj26ZtuMDIGCCsGAQUFBwEB
BCYwJDAiBggrBgEFBQcwAoYWaHR0cDovL3gxLmkubGVuY3Iub3JnLzATBgNVHSAE
DDAKMAgGBmeBDAECATAnBgNVHR8EIDAeMBygGqAYhhZodHRwOi8veDEuYy5sZW5j
ci5vcmcvMA0GCSqGSIb3DQEBCwUAA4ICAQBnE0hGINKsCYWi0Xx1ygxD5qihEjZ0
RI3tTZz1wuATH3ZwYPIp97kWEayanD1j0cDhIYzy4CkDo2jB8D5t0a6zZWzlr98d
AQFNh8uKJkIHdLShy+nUyeZxc5bNeMp1Lu0gSzE4McqfmNMvIpeiwWSYO9w82Ob8
otvXcO2JUYi3svHIWRm3+707DUbL51XMcY2iZdlCq4Wa9nbuk3WTU4gr6LY8MzVA
aDQG2+4U3eJ6qUF10bBnR1uuVyDYs9RhrwucRVnfuDj29CMLTsplM5f5wSV5hUpm
Uwp/vV7M4w4aGunt74koX71n4EdagCsL/Yk5+mAQU0+tue0JOfAV/R6t1k+Xk9s2
HMQFeoxppfzAVC04FdG9M+AC2JWxmFSt6BCuh3CEey3fE52Qrj9YM75rtvIjsm/1
Hl+u//Wqxnu1ZQ4jpa+VpuZiGOlWrqSP9eogdOhCGisnyewWJwRQOqK16wiGyZeR
xs/Bekw65vwSIaVkBruPiTfMOo0Zh4gVa8/qJgMbJbyrwwG97z/PRgmLKCDl8z3d
tA0Z7qq7fta0Gl24uyuB05dqI5J1LvAzKuWdIjT1tP8qCoxSE/xpix8hX2dt3h+/
jujUgFPFZ0EVZ0xSyBNRF3MboGZnYXFUxpNjTWPKpagDHJQmqrAcDmWJnMsFY3jS
u1igv3OefnWjSQ==
-----END CERTIFICATE-----";

    }
}
