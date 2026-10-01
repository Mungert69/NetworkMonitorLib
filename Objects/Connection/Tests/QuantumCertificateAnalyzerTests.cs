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

        [Theory]
        [InlineData("Verify return code: 0 (ok)", true)]
        [InlineData("Verify return code: 21 (unable to verify the first certificate)", false)]
        [InlineData("verify error:num=20:unable to get local issuer certificate", false)]
        [InlineData("Verification error: certificate has expired", false)]
        public void TrustDiagnostics_DoNotSuppressCertificateClassification(string verification, bool trusted)
        {
            var output = $"{CreateSelfSignedPem()}\n{verification}";
            Assert.True(QuantumCertificateAnalyzer.TryBuildSummary(output, out var summary));
            Assert.Equal(trusted, summary.IsTrusted);
            Assert.Equal(!trusted, summary.ToSummaryString().Contains("Certificate trust: not trusted"));
            Assert.Contains("SigAlg=", summary.ToSummaryString());
            Assert.False(summary.IsQuantumSafeCertificate);
        }

        [Fact]
        public void MissingVerificationResult_DoesNotInventTrust()
        {
            Assert.True(QuantumCertificateAnalyzer.TryBuildSummary(CreateSelfSignedPem(), out var summary));
            Assert.Null(summary.IsTrusted);
            Assert.DoesNotContain("Certificate trust:", summary.ToSummaryString());
        }

        [Fact]
        public void UntrustedQuantumCertificate_RemainsQuantumSafe()
        {
            var summary = new QuantumCertificateSummary
            {
                SignatureQuantumSafe = true,
                SignatureAlgorithm = "ML-DSA-65",
                IsTrusted = false
            };
            Assert.True(summary.IsQuantumSafeCertificate);
            Assert.Contains("SigAlg=ML-DSA-65", summary.ToSummaryString());
            Assert.Contains("Certificate trust: not trusted", summary.ToSummaryString());
        }

        [Fact]
        public void FinalVerificationResult_TakesPrecedence()
        {
            var output = $"verify error:num=20:initial failure\n{CreateSelfSignedPem()}\n" +
                "Verify return code: 20 (initial failure)\nVerify return code: 0 (ok)";
            Assert.True(QuantumCertificateAnalyzer.TryBuildSummary(output, out var summary));
            Assert.True(summary.IsTrusted);
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
