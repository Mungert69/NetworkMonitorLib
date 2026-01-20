using System;
using System.Collections.Generic;
using System.IO;
using NetworkMonitor.Connection;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class ConnectHelperTests
    {
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
