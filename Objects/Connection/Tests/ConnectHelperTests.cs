using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Connection;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class ConnectHelperTests
    {
        [Fact]
        public void GetAlgorithmInfoList_FallsBackToCurvesFile_WhenRuntimeProbeUnavailable()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "nm-groups-fallback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllLines(Path.Combine(tempDir, "AlgoTable.csv"), new[]
                {
                    "AlgorithmName,DefaultID,Enabled,EnvironmentVariable,AddEnv",
                    "mlkem768,0x11,no,,no",
                    "bikel1,0x12,no,,no"
                });
                File.WriteAllLines(Path.Combine(tempDir, "curves"), new[] { "mlkem768" });

                var cfg = new NetConnectConfig(new ConfigurationBuilder().Build(), "TestSection")
                {
                    OqsProviderPath = tempDir,
                    // Intentionally invalid so runtime probe is skipped and fallback is used.
                    CommandPath = Path.Combine(tempDir, "missing-bin-dir")
                };

                var list = ConnectHelper.GetAlgorithmInfoList(cfg);
                Assert.True(list.Single(a => a.AlgorithmName == "mlkem768").Enabled);
                Assert.False(list.Single(a => a.AlgorithmName == "bikel1").Enabled);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void GetAlgorithmInfoList_PrefersRuntimeSupportedGroups_OverCurvesFile()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // The test creates a Unix-style executable script for the probe binary.
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "nm-groups-runtime-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                File.WriteAllLines(Path.Combine(tempDir, "AlgoTable.csv"), new[]
                {
                    "AlgorithmName,DefaultID,Enabled,EnvironmentVariable,AddEnv",
                    "mlkem768,0x11,no,,no",
                    "bikel1,0x12,no,,no"
                });
                // Curves file intentionally conflicts with runtime output.
                File.WriteAllLines(Path.Combine(tempDir, "curves"), new[] { "bikel1" });

                var opensslPath = Path.Combine(tempDir, "openssl");
                File.WriteAllText(opensslPath,
                    "#!/bin/sh\n" +
                    "echo \"x25519:mlkem768\"\n");

                var chmod = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{opensslPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                chmod?.WaitForExit(2000);

                var cfg = new NetConnectConfig(new ConfigurationBuilder().Build(), "TestSection")
                {
                    OqsProviderPath = tempDir,
                    CommandPath = tempDir
                };

                var list = ConnectHelper.GetAlgorithmInfoList(cfg);
                Assert.True(list.Single(a => a.AlgorithmName == "mlkem768").Enabled);
                Assert.False(list.Single(a => a.AlgorithmName == "bikel1").Enabled);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        [Fact]
        public void GetCertificateOidNameMap_ParsesOidNameFormats()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "nm-cert-oids-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var filePath = Path.Combine(tempDir, "cert_oids");
                File.WriteAllLines(filePath, new[]
                {
                    "# comment",
                    "1.2.3.4 alg1",
                    "1.2.3.5|alg2",
                    "1.2.3.6    alg3",
                    "1.2.3.7"
                });

                var map = ConnectHelper.GetCertificateOidNameMap(tempDir);

                Assert.Equal("alg1", map["1.2.3.4"]);
                Assert.Equal("alg2", map["1.2.3.5"]);
                Assert.Equal("alg3", map["1.2.3.6"]);
                Assert.Equal("1.2.3.7", map["1.2.3.7"]);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
    }
}
