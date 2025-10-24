using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using NetworkMonitor.Objects;

namespace NetworkMonitor.Objects.Repository.Helpers
{
    /// <summary>
    /// For Android 5/6 (SDK <= 23) only: validate the server chain by
    /// anchoring to ISRG Root X1 provided by the app (file or embedded PEM).
    /// No device installs and no server changes required.
    /// </summary>
    internal static class LegacyAndroidSslHelper
    {
        private const int LegacyAndroidMaxSdkLevel = 23; // Android 6.0 and below

        private static X509Certificate2? _cachedIsrgRoot;

        internal static void Configure(SystemUrl systemUrl, SslOption sslOption, ILogger? logger)
        {
            if (sslOption == null || systemUrl == null || !sslOption.Enabled) return;
            if (systemUrl.AndroidSdkLevel <= 0 || systemUrl.AndroidSdkLevel > LegacyAndroidMaxSdkLevel) return;

            logger?.LogDebug("Applying legacy Android certificate handling for SDK level {SdkLevel}.", systemUrl.AndroidSdkLevel);

            // Ensure SNI/hostname is set so the right cert is presented & name checks can pass
            if (string.IsNullOrWhiteSpace(sslOption.ServerName))
                sslOption.ServerName = systemUrl.RabbitHostName;

            // Keep policy strict; we’ll decide entirely in the callback
            sslOption.AcceptablePolicyErrors = SslPolicyErrors.None;

            // Legacy-safe minimum
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

            // Always enforce hostname matching
            if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                logger?.LogWarning("Certificate name mismatch.");
                return false;
            }

            // If the platform already trusts it (unlikely on 5/6), accept
            if (errors == SslPolicyErrors.None) return true;

            try
            {
                var leaf = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
                var disposeLeaf = !(certificate is X509Certificate2);

                var chain = new X509Chain();
                var disposeChain = true;

                try
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // legacy devices
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Clear();
                    chain.ChainPolicy.ExtraStore.Clear();

                    var rootCert = GetRootCertificate(systemUrl, logger);
                    chain.ChainPolicy.CustomTrustStore.Add(rootCert);

                    // Reuse any intermediates the platform handed us
                    if (platformChain != null)
                    {
                        foreach (var element in platformChain.ChainElements)
                        {
                            var c = element.Certificate;
                            if (c == null) continue;
                            var t = c.Thumbprint;
                            if (string.IsNullOrEmpty(t)) continue;

                            if (!t.Equals(leaf.Thumbprint, StringComparison.OrdinalIgnoreCase) &&
                                !t.Equals(rootCert.Thumbprint, StringComparison.OrdinalIgnoreCase))
                            {
                                chain.ChainPolicy.ExtraStore.Add(c);
                            }
                        }
                    }

                    var ok = chain.Build(leaf);

                    // Some legacy stacks may still report PartialChain/UntrustedRoot.
                    // Accept if the built chain actually anchors at our ISRG root.
                    if (!ok)
                    {
                        bool onlyUnknownRootOrPartial = chain.ChainStatus.All(s =>
                            s.Status == X509ChainStatusFlags.UntrustedRoot ||
                            s.Status == X509ChainStatusFlags.PartialChain ||
                            s.Status == X509ChainStatusFlags.RevocationStatusUnknown);

                        if (onlyUnknownRootOrPartial)
                        {
                            var anchored = chain.ChainElements
                                .Cast<X509ChainElement>()
                                .Any(e => string.Equals(e.Certificate.Thumbprint, rootCert.Thumbprint, StringComparison.OrdinalIgnoreCase));

                            if (anchored)
                                return true; // accept: we proved the chain terminates at our ISRG root
                        }

                        foreach (var status in chain.ChainStatus)
                            logger?.LogWarning("Certificate chain validation failed: {Status} - {Info}", status.Status, status.StatusInformation);
                    }

                    return ok;
                }
                finally
                {
                    if (disposeChain) chain.Dispose();
                    if (disposeLeaf) leaf.Dispose();
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Certificate validation threw an exception for legacy Android.");
                return false;
            }
        }

        private static X509Certificate2 GetRootCertificate(SystemUrl systemUrl, ILogger? logger)
        {
            if (_cachedIsrgRoot != null) return _cachedIsrgRoot;

            var path = systemUrl.LegacyAndroidRootCertPath; // your app-provided file (PEM/DER)
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

            // Fallback to embedded PEM (keeps working even if the file is missing)
            _cachedIsrgRoot = X509Certificate2.CreateFromPem(IsrgRootX1Pem);
            return _cachedIsrgRoot;
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
    }
}
