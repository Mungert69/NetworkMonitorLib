using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NetworkMonitor.Connection;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class QuantumCertificateAnalyzerTests
    {
        [Fact]
        public void TryBuildSummary_ReturnsFalse_WhenNoCertificate()
        {
            var ok = QuantumCertificateAnalyzer.TryBuildSummary("no certs here", out var summary);

            Assert.False(ok);
            Assert.Null(summary);
        }

        [Fact]
        public void TryBuildSummary_ParsesLeafCertificate()
        {
            var pem = CreateSelfSignedPem();
            var output = $"handshake\n{pem}\nend";

            var ok = QuantumCertificateAnalyzer.TryBuildSummary(output, out var summary);

            Assert.True(ok);
            Assert.NotNull(summary);
            Assert.Equal("example.com", summary.Subject);
            Assert.False(summary.SignatureQuantumSafe);
            Assert.False(summary.PublicKeyQuantumSafe);
            Assert.Equal(1, summary.ChainLength);
        }

        private static string CreateSelfSignedPem()
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(
                "CN=example.com",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("example.com");
            req.CertificateExtensions.Add(san.Build());
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));

            var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            var notAfter = DateTimeOffset.UtcNow.AddDays(30);
            using var cert = req.CreateSelfSigned(notBefore, notAfter);

            return cert.ExportCertificatePem();
        }
    }
}
