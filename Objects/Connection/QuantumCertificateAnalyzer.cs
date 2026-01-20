using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace NetworkMonitor.Connection
{
    public sealed class QuantumCertificateSummary
    {
        public string Subject { get; init; } = "";
        public string Issuer { get; init; } = "";
        public DateTime NotBeforeUtc { get; init; }
        public DateTime NotAfterUtc { get; init; }
        public string SignatureAlgorithm { get; init; } = "";
        public string SignatureAlgorithmOid { get; init; } = "";
        public string PublicKeyAlgorithm { get; init; } = "";
        public string PublicKeyAlgorithmOid { get; init; } = "";
        public bool SignatureQuantumSafe { get; init; }
        public bool PublicKeyQuantumSafe { get; init; }
        public int ChainLength { get; init; }

        public bool IsQuantumSafeCertificate => SignatureQuantumSafe || PublicKeyQuantumSafe;

        public string ToSummaryString()
        {
            var expires = NotAfterUtc == default
                ? "unknown"
                : NotAfterUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var pqc = IsQuantumSafeCertificate ? "yes" : "no";
            var sigPqc = SignatureQuantumSafe ? "yes" : "no";
            var keyPqc = PublicKeyQuantumSafe ? "yes" : "no";

            return $"Certificate PQC: {pqc} (sig={sigPqc}, key={keyPqc}); " +
                   $"SigAlg={SignatureAlgorithm}; KeyAlg={PublicKeyAlgorithm}; " +
                   $"Expires={expires}; Subject={Subject}; Issuer={Issuer}; ChainLength={ChainLength}";
        }
    }

    public static class QuantumCertificateAnalyzer
    {
        private const string PemBegin = "-----BEGIN CERTIFICATE-----";
        private const string PemEnd = "-----END CERTIFICATE-----";

        private static readonly string[] PqcNameTokens = new[]
        {
            "dilithium", "ml-dsa", "mldsa", "falcon", "sphincs", "slh-dsa", "slhdsa",
            "slhdsasha", "slhdsashake", "mayo", "cross", "snova", "ov_",
            "xmss", "hss", "lms", "picnic", "rainbow"
        };

        private static readonly string[] PqcOidPrefixes = new[]
        {
            "1.3.6.1.4.1.2.267",        // OQS/IBM test OID space commonly used by PQC providers
            "1.3.9999.",                // OQS default OID space for PQC signatures
            "1.3.6.1.4.1.62245.",       // CROSS family OIDs (OQS provider)
            "2.16.840.1.101.3.4.3.",    // NIST PQ signature OIDs (ML-DSA, SLH-DSA)
            "1.3.6.1.4.1.22554.",       // KEM encoder OIDs (if enabled)
            "1.3.6.1.4.1.42235.6"       // SecP384r1MLKEM1024 OID (if enabled)
        };

        public static bool TryBuildSummary(string opensslOutput, out QuantumCertificateSummary summary)
        {
            return TryBuildSummary(opensslOutput, null, out summary);
        }

        public static bool TryBuildSummary(
            string opensslOutput,
            IReadOnlyDictionary<string, string>? allowedOids,
            out QuantumCertificateSummary summary)
        {
            summary = null!;
            if (string.IsNullOrWhiteSpace(opensslOutput)) return false;

            var certs = ExtractCertificates(opensslOutput);
            if (certs.Count == 0) return false;

            try
            {
                using var leaf = certs[0];

                var subject = leaf.GetNameInfo(X509NameType.DnsName, false);
                if (string.IsNullOrWhiteSpace(subject)) subject = leaf.Subject ?? "";

                var issuer = leaf.GetNameInfo(X509NameType.DnsName, true);
                if (string.IsNullOrWhiteSpace(issuer)) issuer = leaf.Issuer ?? "";

                var sigAlgName = leaf.SignatureAlgorithm?.FriendlyName ?? "";
                var sigAlgOid = leaf.SignatureAlgorithm?.Value ?? "";
                var keyAlgName = leaf.PublicKey?.Oid?.FriendlyName ?? "";
                var keyAlgOid = leaf.PublicKey?.Oid?.Value ?? "";

                sigAlgName = ResolveAlgorithmName(sigAlgName, sigAlgOid, allowedOids);
                keyAlgName = ResolveAlgorithmName(keyAlgName, keyAlgOid, allowedOids);

                var sigPqc = IsQuantumSafeAlgorithm(sigAlgName, sigAlgOid, allowedOids);
                var keyPqc = IsQuantumSafeAlgorithm(keyAlgName, keyAlgOid, allowedOids);

                summary = new QuantumCertificateSummary
                {
                    Subject = subject,
                    Issuer = issuer,
                    NotBeforeUtc = leaf.NotBefore.ToUniversalTime(),
                    NotAfterUtc = leaf.NotAfter.ToUniversalTime(),
                    SignatureAlgorithm = sigAlgName,
                    SignatureAlgorithmOid = sigAlgOid,
                    PublicKeyAlgorithm = keyAlgName,
                    PublicKeyAlgorithmOid = keyAlgOid,
                    SignatureQuantumSafe = sigPqc,
                    PublicKeyQuantumSafe = keyPqc,
                    ChainLength = certs.Count
                };

                return true;
            }
            finally
            {
                for (int i = 1; i < certs.Count; i++)
                {
                    certs[i].Dispose();
                }
            }
        }

        private static List<X509Certificate2> ExtractCertificates(string output)
        {
            var certs = new List<X509Certificate2>();
            int idx = 0;
            while (idx >= 0 && idx < output.Length)
            {
                var start = output.IndexOf(PemBegin, idx, StringComparison.Ordinal);
                if (start < 0) break;
                var end = output.IndexOf(PemEnd, start, StringComparison.Ordinal);
                if (end < 0) break;

                var pem = output.Substring(start, end + PemEnd.Length - start);
                try
                {
                    var cert = X509Certificate2.CreateFromPem(pem);
                    certs.Add(cert);
                }
                catch
                {
                    // Ignore malformed PEM blocks and keep searching.
                }

                idx = end + PemEnd.Length;
            }
            return certs;
        }

        private static bool IsQuantumSafeAlgorithm(
            string? name,
            string? oid,
            IReadOnlyDictionary<string, string>? allowedOids)
        {
            var value = name ?? "";
            if (!string.IsNullOrWhiteSpace(value))
            {
                foreach (var token in PqcNameTokens)
                {
                    if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            var oidValue = oid ?? "";
            if (allowedOids != null && allowedOids.Count > 0)
            {
                if (allowedOids.ContainsKey(oidValue))
                    return true;
            }
            foreach (var prefix in PqcOidPrefixes)
            {
                if (oidValue.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string ResolveAlgorithmName(
            string? friendlyName,
            string? oid,
            IReadOnlyDictionary<string, string>? allowedOids)
        {
            var name = friendlyName ?? "";
            var oidValue = oid ?? "";
            var needsOverride = string.IsNullOrWhiteSpace(name) ||
                                string.Equals(name, oidValue, StringComparison.Ordinal) ||
                                string.Equals(name, "unknown", StringComparison.OrdinalIgnoreCase);

            if (needsOverride && allowedOids != null && allowedOids.TryGetValue(oidValue, out var mapped) &&
                !string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            if (string.IsNullOrWhiteSpace(name))
                return string.IsNullOrWhiteSpace(oidValue) ? "unknown" : oidValue;

            return name;
        }
    }
}
