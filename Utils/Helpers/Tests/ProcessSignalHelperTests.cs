using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class ProcessSignalHelperTests
{
    [Fact]
    public void SendCtrlCSignal_TerminatesUnixProcess()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-c", "trap 'exit 0' SIGINT; while true; do sleep 1; done" },
                UseShellExecute = false
            }
        };

        process.Start();

        try
        {
            ProcessSignalHelper.SendCtrlCSignal(process);
            Assert.True(process.WaitForExit(5000), "Process did not exit after Ctrl+C signal.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }
}
