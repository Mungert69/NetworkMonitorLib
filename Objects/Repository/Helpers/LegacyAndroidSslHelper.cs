using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using NetworkMonitor.Objects;
using System.IO;

namespace NetworkMonitor.Objects.Repository.Helpers
{
    internal static class LegacyAndroidSslHelper
    {
        private const int LegacyAndroidMaxSdkLevel = 23;

        internal static void Configure(SystemUrl systemUrl, SslOption sslOption, ILogger? logger)
        {
            if (sslOption == null || systemUrl == null)
            {
                return;
            }

            if (!sslOption.Enabled)
            {
                return;
            }

            if (systemUrl.AndroidSdkLevel <= 0 || systemUrl.AndroidSdkLevel > LegacyAndroidMaxSdkLevel)
            {
                return;
            }

            logger?.LogDebug("Applying legacy Android certificate handling for SDK level {SdkLevel}.", systemUrl.AndroidSdkLevel);

            sslOption.AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateChainErrors;
            sslOption.CertificateValidationCallback = (sender, certificate, chain, errors) =>
                ValidateWithCustomTrustStore(certificate, chain, errors, logger, systemUrl);
        }

        private static bool ValidateWithCustomTrustStore(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors, ILogger? logger, SystemUrl systemUrl)
        {
            if (certificate == null)
            {
                logger?.LogWarning("Certificate validation failed: no certificate was provided.");
                return false;
            }

            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            try
            {
                X509Certificate2 serverCertificate;
                var disposeServerCertificate = false;
                if (certificate is X509Certificate2 existing)
                {
                    serverCertificate = existing;
                }
                else
                {
                    serverCertificate = new X509Certificate2(certificate);
                    disposeServerCertificate = true;
                }

                var validationChain = chain ?? new X509Chain();
                var disposeChain = chain == null;

                try
                {
                    validationChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    validationChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    validationChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    validationChain.ChainPolicy.CustomTrustStore.Clear();
                    validationChain.ChainPolicy.ExtraStore.Clear();

                    using var rootCert = GetRootCertificate(systemUrl, logger);
                    validationChain.ChainPolicy.CustomTrustStore.Add(rootCert);
                    var rootThumbprint = rootCert?.Thumbprint ?? string.Empty;

                    if (chain != null)
                    {
                        foreach (var element in chain.ChainElements)
                        {
                            var candidate = element.Certificate;
                            if (candidate == null)
                            {
                                continue;
                            }

                            var thumbprint = candidate.Thumbprint;
                            if (string.IsNullOrEmpty(thumbprint))
                            {
                                continue;
                            }

                            if (!thumbprint.Equals(serverCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase) &&
                                !thumbprint.Equals(rootThumbprint, StringComparison.OrdinalIgnoreCase) &&
                                !validationChain.ChainPolicy.ExtraStore.Contains(candidate))
                            {
                                validationChain.ChainPolicy.ExtraStore.Add(candidate);
                            }
                        }
                    }

                    var isValid = validationChain.Build(serverCertificate);
                    if (!isValid)
                    {
                        foreach (var status in validationChain.ChainStatus)
                        {
                            logger?.LogWarning("Certificate chain validation failed: {Status} - {Info}", status.Status, status.StatusInformation);
                        }
                    }

                    return isValid;
                }
                finally
                {
                    if (disposeChain)
                    {
                        validationChain.Dispose();
                    }

                    if (disposeServerCertificate)
                    {
                        serverCertificate.Dispose();
                    }
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
            var path = systemUrl.LegacyAndroidRootCertPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        return X509CertificateLoader.LoadCertificateFromFile(path);
                    }
                    logger?.LogWarning("Legacy Android root certificate was not found at {Path}. Falling back to embedded copy.", path);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to load legacy Android root certificate from {Path}. Falling back to embedded copy.", path);
                }
            }
            return X509Certificate2.CreateFromPem(IsrgRootX1Pem);
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
