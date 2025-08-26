// PortableProcessRunner.cs
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace NetworkMonitor.Connection;

public class PortableProcessRunner : IPlatformProcessRunner
{
    private readonly ILogger _logger;
    public PortableProcessRunner(ILogger logger) => _logger = logger;

    public async Task<string> RunAsync(
        string executablePath,
        string arguments,
        string workingDirectory,
        IDictionary<string, string>? envVars,
        CancellationToken token)
    {
        var output = new StringBuilder();
        var error  = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName               = executablePath,
            Arguments              = arguments ?? string.Empty,
            WorkingDirectory       = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true
        };

        if (envVars != null)
            foreach (var kv in envVars)
                psi.Environment[kv.Key] = kv.Value;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // cooperative cancellation
        using var reg = token.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        _logger.LogInformation("Starting: {Exe} {Args}", psi.FileName, psi.Arguments);
        proc.Start();

        var stdOutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
            {
                token.ThrowIfCancellationRequested();
                output.AppendLine(line);
                _logger.LogTrace("[STDOUT] {Line}", line);
            }
        }, token);

        var stdErrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync()) != null)
            {
                token.ThrowIfCancellationRequested();
                error.AppendLine(line);
                _logger.LogTrace("[STDERR] {Line}", line);
            }
        }, token);

        await Task.WhenAll(stdOutTask, stdErrTask);
        proc.WaitForExit();

        _logger.LogInformation("Exit: {Code}", proc.ExitCode);
        _logger.LogDebug("Output:\n{Err}\n{Out}", error, output);

        return $"{error} : {output}";
    }
}
