using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
namespace NetworkMonitor.Utils.Helpers;

public class LibraryHelper
{
    public static string SetLDLibraryPath(string libraryPath)
    {
        var outputStr = new StringBuilder();
        try
        {
            string ldLibraryPath = $"LD_LIBRARY_PATH={libraryPath}";
            Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libraryPath);
            outputStr.AppendLine($"Set LD_LIBRARY_PATH to: {libraryPath}");
            Environment.SetEnvironmentVariable("OPENSSL_MODULES", libraryPath);
            outputStr.AppendLine($"Set OPENSSL_MODULES to: {libraryPath}");
        }
        catch (Exception ex)
        {
            outputStr.AppendLine($"Failed to set LD_LIBRARY_PATH: {ex.Message}");
        }
        return outputStr.ToString();
    }
}