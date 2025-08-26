// AndroidProcWrapperRunner.cs
#if ANDROID
using Android.App;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace NetworkMonitor.Connection;

public class AndroidProcWrapperRunner : IPlatformProcessRunner
{
    private readonly ILogger _logger;
    private readonly string _nativeDir;

    /// <param name="nativeDir">
    /// If null/empty, defaults to Application.Context.ApplicationInfo.NativeLibraryDir
    /// (That is where MAUI places .so files for each ABI and is executable on Android 14+.)
    /// </param>
    public AndroidProcWrapperRunner(ILogger logger, string? nativeDir = null)
    {
        _logger   = logger;
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
        // Resolve the executable path; on Android we want it in nativeLibraryDir
        string exeFullPath = Path.IsPathRooted(executablePath)
            ? executablePath
            : Path.Combine(_nativeDir, executablePath);

        // Ensure env vars are inherited by the forked child (procwrapper uses fork/execv)
        // Important for OpenSSL providers & local libs
        // LD_LIBRARY_PATH must include _nativeDir; OPENSSL_MODULES should point there too.
        MergeEnv("LD_LIBRARY_PATH", _nativeDir);
        SetEnvIfPresent("OPENSSL_MODULES", _nativeDir);

        if (envVars is not null)
        {
            foreach (var kv in envVars)
            {
                // allow overrides or additions from the caller
                if (kv.Key.Equals("LD_LIBRARY_PATH", StringComparison.Ordinal))
                    MergeEnv("LD_LIBRARY_PATH", kv.Value);
                else
                    Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }

        // Build argv[] for the native wrapper
        string[] argv = BuildArgvVector(exeFullPath, arguments);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var ps = new NativeProc.ProcessStream { Logger=_logger };

        // capture lines as they stream in
        ps.OnStdoutLine += line => { lock (stdout) stdout.AppendLine(line); _logger.LogTrace("[STDOUT] {Line}", line); };
        ps.OnStderrLine += line => { lock (stderr) stderr.AppendLine(line); _logger.LogTrace("[STDERR] {Line}", line); };
        ps.OnExited     += ec   => _logger.LogInformation("Process exited with code {Code}", ec);

        // Cancellation: if requested, stop the process (SIGTERM/SIGKILL via wrapper)
        using var ctr = token.Register(() =>
        {
            try { ps.Stop(); } catch { /* ignore */ }
        });

        _logger.LogInformation("Android(procwrapper) exec: {Exe} {Args}", exeFullPath, arguments);

        // Start the loader+args OR the binary directly; on Android we start the binary directly
        if (!ps.Start(exeFullPath, argv.Skip(1).ToArray())) // argv[0] is the exe itself
        {
            _logger.LogError("Failed to start process: {Path}", exeFullPath);
            return $"Failed to start: {exeFullPath}";
        }

        var exit = await ps.WaitForExitAsync(50, token);
        // NEW: wait for reader to fully drain both pipes (matches native changes)
        await ps.WaitForDrainAsync();
        _logger.LogInformation("Exit code: {Exit}", exit);

        return $"{stderr} : {stdout}";
    }

    private static string[] BuildArgvVector(string exe, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return new[] { exe };

        // Simple tokenizer. If you need full quote/escape support, replace with a robust splitter.
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var argv = new string[parts.Length + 1];
        argv[0] = exe;
        Array.Copy(parts, 0, argv, 1, parts.Length);
        return argv;
    }

    private static void MergeEnv(string key, string prependDir)
    {
        var old = Environment.GetEnvironmentVariable(key);
        string val = string.IsNullOrEmpty(old) ? prependDir : $"{prependDir}:{old}";
        Environment.SetEnvironmentVariable(key, val);
    }

    private static void SetEnvIfPresent(string key, string value)
    {
        // set it unconditionally — harmless for non-OpenSSL launches
        Environment.SetEnvironmentVariable(key, value);
    }
}
#endif
