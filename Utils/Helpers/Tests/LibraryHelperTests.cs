using System;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class LibraryHelperTests
{
    [Fact]
    public void SetLDLibraryPath_SetsEnvironmentVariables()
    {
        var originalLdLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        var originalOpenSslModules = Environment.GetEnvironmentVariable("OPENSSL_MODULES");

        try
        {
            var output = LibraryHelper.SetLDLibraryPath("/tmp/libs");

            Assert.Contains("Set LD_LIBRARY_PATH to: /tmp/libs", output);
            Assert.Contains("Set OPENSSL_MODULES to: /tmp/libs", output);
            Assert.Equal("/tmp/libs", Environment.GetEnvironmentVariable("LD_LIBRARY_PATH"));
            Assert.Equal("/tmp/libs", Environment.GetEnvironmentVariable("OPENSSL_MODULES"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", originalLdLibraryPath);
            Environment.SetEnvironmentVariable("OPENSSL_MODULES", originalOpenSslModules);
        }
    }
}
