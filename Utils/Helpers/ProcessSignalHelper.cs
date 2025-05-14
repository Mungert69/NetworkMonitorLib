using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
namespace NetworkMonitor.Utils.Helpers;
public static class ProcessSignalHelper
{
    // Public method to send a Ctrl+C signal to a process
    public static void SendCtrlCSignal(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            SendCtrlCWindows(process);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            SendCtrlCUnix(process);
        }
        else
        {
            throw new PlatformNotSupportedException("Sending Ctrl+C is not supported on this platform.");
        }
    }

    // Windows-specific implementation
    private static void SendCtrlCWindows(Process process)
    {
        if (!AttachConsole((uint)process.Id))
        {
            throw new InvalidOperationException("Failed to attach to the console of the target process.");
        }

        try
        {
            if (!SetConsoleCtrlHandler(null, true))
            {
                throw new InvalidOperationException("Failed to disable Ctrl+C handling in the parent process.");
            }

            if (!GenerateConsoleCtrlEvent(ConsoleCtrlEvent.CTRL_C, 0))
            {
                throw new InvalidOperationException("Failed to send Ctrl+C signal to the process.");
            }
        }
        finally
        {
            SetConsoleCtrlHandler(null, false); // Restore the default Ctrl+C handling
            FreeConsole();
        }
    }

    // Unix-specific implementation
    private static void SendCtrlCUnix(Process process)
    {
        if (kill(process.Id, SIGINT) != 0)
        {
            throw new InvalidOperationException($"Failed to send SIGINT to process with ID {process.Id}.");
        }
    }

    // Windows DLL imports and constants
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handlerRoutine, bool add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(ConsoleCtrlEvent ctrlEvent, uint processGroupId);

    private delegate bool ConsoleCtrlHandler(ConsoleCtrlEvent? ctrlType);

    private enum ConsoleCtrlEvent
    {
        CTRL_C = 0,
        CTRL_BREAK = 1
    }

    // Unix DLL imports and constants
    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    private const int SIGINT = 2;
}
