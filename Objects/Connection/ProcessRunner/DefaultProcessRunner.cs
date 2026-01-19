using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using System.Text;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;

namespace NetworkMonitor.Connection;

public class DefaultProcessRunner : IPlatformProcessRunner
{
    private readonly ILogger _logger;
    public DefaultProcessRunner(ILogger logger) => _logger = logger;

    public async Task<string> RunAsync(string executablePath, string arguments, string workingDirectory,
                                       IDictionary<string, string>? envVars, CancellationToken token)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();

        bool closeStdin = envVars != null &&
                          envVars.TryGetValue("NM_CLOSE_STDIN", out var closeStdinRaw) &&
                          closeStdinRaw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = closeStdin,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        if (envVars != null)
        {
            foreach (var kv in envVars)
                psi.EnvironmentVariables[kv.Key] = kv.Value;
        }

        using var proc = new Process { StartInfo = psi };
        token.Register(() => { try { if (!proc.HasExited) proc.Kill(); } catch { } });

        proc.Start();
        if (closeStdin)
        {
            proc.StandardInput.Close(); // Ensure EOF so openssl s_client exits after handshake.
        }

        await Task.WhenAll(
            ReadStreamAsync(proc.StandardOutput, output, token),
            ReadStreamAsync(proc.StandardError, error, token));

        proc.WaitForExit();
        _logger.LogDebug($"Output: {error} : {output}");
        return $"{error} : {output}";
    }

    private static async Task ReadStreamAsync(StreamReader reader, StringBuilder sb, CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            ct.ThrowIfCancellationRequested();
            sb.AppendLine(line);
        }
    }
}
