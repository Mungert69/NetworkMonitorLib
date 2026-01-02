// BusyboxProcessRunner.cs
#if ANDROID
using Android.App;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace NetworkMonitor.Connection;

public sealed class BusyboxProcessRunner : IPlatformProcessRunner
{
    private readonly ILogger _logger;
    private readonly string _nativeDir;

    public BusyboxProcessRunner(ILogger logger, string? nativeDir = null)
    {
        _logger = logger;
        _nativeDir = string.IsNullOrWhiteSpace(nativeDir)
            ? Application.Context.ApplicationInfo!.NativeLibraryDir!
            : nativeDir!;
    }

    public async Task<string> RunAsync(
        string executablePath,
        string arguments,
        string workingDirectory,
        IDictionary<string, string>? envVars,
        CancellationToken token)
    {
        string exeFullPath = Path.IsPathRooted(executablePath)
            ? executablePath
            : Path.Combine(_nativeDir, executablePath);

        // Ensure dlopen() search path is set for native deps.
        MergeEnv("LD_LIBRARY_PATH", _nativeDir);
        SetEnvIfPresent("OPENSSL_MODULES", _nativeDir);

        if (envVars is not null)
        {
            foreach (var kv in envVars)
            {
                if (kv.Key.Equals("LD_LIBRARY_PATH", System.StringComparison.Ordinal))
                    MergeEnv("LD_LIBRARY_PATH", kv.Value);
                else
                    System.Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }

        NormalizeLdLibraryPath(_nativeDir, workingDirectory);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var ps = new NativeProc.ProcessStream { Logger = _logger };

        ps.OnStdoutLine += line =>
        {
            lock (stdout) stdout.AppendLine(line);
            _logger.LogDebug("[STDOUT] {Line}", line);
        };
        ps.OnStderrLine += line =>
        {
            lock (stderr) stderr.AppendLine(line);
            _logger.LogDebug("[STDERR] {Line}", line);
        };
        ps.OnExited += ec => _logger.LogInformation("Process exited with code {Code}", ec);

        using var ctr = token.Register(() => { try { ps.Stop(); } catch { } });

        string[] launchArgv = NetworkMonitor.Utils.Argv.Tokenize(arguments);
        string argv0Override;

        if (launchArgv.Length == 0)
        {
            argv0Override = "busybox";
            _logger.LogInformation("Busybox exec: no applet provided, using default list.");
        }
        else
        {
            argv0Override = launchArgv[0];
            launchArgv = launchArgv.Skip(1).ToArray();
            if (argv0Override.Equals("libbusybox_exec.so", System.StringComparison.OrdinalIgnoreCase))
                argv0Override = "busybox";

            _logger.LogInformation("Busybox exec: argv0={Applet} args={Args}",
                argv0Override,
                NetworkMonitor.Utils.Argv.ForLog(launchArgv));
        }

        _logger.LogInformation("Android(busybox) exec: {Exe} {Args}", exeFullPath, NetworkMonitor.Utils.Argv.ForLog(launchArgv));

        var previousDir = Environment.CurrentDirectory;
        var switchedDir = false;
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            try
            {
                Environment.CurrentDirectory = workingDirectory;
                switchedDir = true;
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set working directory to {Dir}", workingDirectory);
            }
        }

        if (!ps.Start(exeFullPath, launchArgv, argv0Override))
        {
            _logger.LogError("Failed to start process: {Path}", exeFullPath);
            return $"Failed to start: {exeFullPath}";
        }

        if (switchedDir)
        {
            try { Environment.CurrentDirectory = previousDir; } catch { /* ignore */ }
        }

        var exit = await ps.WaitForExitAsync(50, token);
        await ps.WaitForDrainAsync();
        string stderrText;
        lock (stderr) stderrText = stderr.ToString();
        string stdoutText;
        lock (stdout) stdoutText = stdout.ToString();

        if (exit != 0)
        {
            _logger.LogInformation("stderr snapshot:\n{Stderr}", stderrText);
            if (!string.IsNullOrWhiteSpace(stdoutText))
                _logger.LogInformation("stdout snapshot:\n{Stdout}", stdoutText);
        }
        else
        {
            _logger.LogDebug("stderr snapshot:\n{Stderr}", stderrText);
            if (!string.IsNullOrWhiteSpace(stdoutText))
                _logger.LogDebug("stdout snapshot:\n{Stdout}", stdoutText);
        }

        _logger.LogInformation("Exit code: {Exit}", exit);

        return $"{stderr} : {stdout}";
    }

    private static void MergeEnv(string key, string prependDir)
    {
        var old = System.Environment.GetEnvironmentVariable(key);
        string val = string.IsNullOrEmpty(old) ? prependDir : $"{prependDir}:{old}";
        System.Environment.SetEnvironmentVariable(key, val);
    }

    private static void SetEnvIfPresent(string key, string value)
        => System.Environment.SetEnvironmentVariable(key, value);

    private static void NormalizeLdLibraryPath(params string[] preferredDirs)
    {
        var existing = System.Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        var ordered = new List<string>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            var trimmed = dir.Trim();
            if (seen.Add(trimmed))
                ordered.Add(trimmed);
        }

        foreach (var dir in preferredDirs)
            Add(dir);

        if (!string.IsNullOrWhiteSpace(existing))
        {
            foreach (var part in existing.Split(':', System.StringSplitOptions.RemoveEmptyEntries))
                Add(part);
        }

        if (ordered.Count > 0)
            System.Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", string.Join(':', ordered));
    }
}
#endif
