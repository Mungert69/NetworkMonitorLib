// AndroidProcWrapperRunner.cs
#if ANDROID
using Android.App;
using Android.OS;
// Don't import Java.IO wholesale; alias the specific type we need:
using JFile = Java.IO.File;
using IOFile = System.IO.File;

using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    /// (where MAUI places .so files and which is exec-mounted on Android 14+)
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
        // Resolve the executable path; on Android we expect it in nativeLibraryDir
        string exeFullPath = System.IO.Path.IsPathRooted(executablePath)
            ? executablePath
            : System.IO.Path.Combine(_nativeDir, executablePath);

        // 🔎 Pre-flight diagnostics (what did Play actually give us?)
        DumpNativeSetup(exeFullPath);

        // Ensure env vars are inherited by the fork/execv (procwrapper)
        MergeEnv("LD_LIBRARY_PATH", _nativeDir);
        SetEnvIfPresent("OPENSSL_MODULES", _nativeDir); // harmless if not OpenSSL

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

        // Build argv[] for the native wrapper
        string[] argv = BuildArgvVector(exeFullPath, arguments);

        // 🧪 Last mile file checks (exists/exec) and best-effort +x
        PreflightExecutable(exeFullPath);

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

        // Start the binary directly; argv[0] is the exe itself
        if (!ps.Start(exeFullPath, argv.Skip(1).ToArray()))
        {
            _logger.LogError("Failed to start process: {Path}", exeFullPath);
            return $"Failed to start: {exeFullPath}";
        }

        var exit = await ps.WaitForExitAsync(50, token);
        await ps.WaitForDrainAsync(); // ensure trailing output is captured
        _logger.LogInformation("Exit code: {Exit}", exit);

        return $"{stderr} : {stdout}";
    }

    private void DumpNativeSetup(string exeFullPath)
    {
        // Core locations/ABIs
        _logger.LogInformation("NativeLibraryDir = {Dir}", _nativeDir);
        _logger.LogInformation("Build.SUPPORTED_ABIS = {Abis}", string.Join(",", Build.SupportedAbis ?? new string[0]));
        _logger.LogInformation("Exec candidate = {Exe}", exeFullPath);

        // List all files under native dir with permissions
        try
        {
            var dir = new JFile(_nativeDir);
            var files = dir.ListFiles();
            if (files is null)
            {
                _logger.LogWarning("ListFiles() returned null for {Dir}", _nativeDir);
            }
            else
            {
                foreach (var f in files)
                {
                    _logger.LogInformation(" • {Name} size={Size} R={R} X={X}",
                        f.Name, f.Length(), f.CanRead(), f.CanExecute());
                }
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate {Dir}", _nativeDir);
        }

        // Check expected libs; include both naming variants so you see what's present
        string[] expected = new[]
        {
            "libprocwrapper.so",
            "libopenssl_exec.so",     // recommended name for the exec payload
            "libnmap_exec.so",        // older name (if still used)
            "libssl.so",
            "libcrypto.so",
            "openssl.so",
            "libc++_shared.so",
            "oqsprovider.so",         // your provider as shipped
            "liboqsprovider.so"       // if you ever rename to lib*.so
        };

        foreach (var name in expected.Distinct())
        {
            var p = System.IO.Path.Combine(_nativeDir, name);
            var jf = new JFile(p);
            _logger.LogInformation("Check {Name}: exists={E} size={S} R={R} X={X}",
                name, jf.Exists(), jf.Length(), jf.CanRead(), jf.CanExecute());
        }

        // Current env that will be inherited by the child
        var ldlp = System.Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "(null)";
        var mods = System.Environment.GetEnvironmentVariable("OPENSSL_MODULES") ?? "(null)";
        _logger.LogInformation("LD_LIBRARY_PATH = {LD}", ldlp);
        _logger.LogInformation("OPENSSL_MODULES = {MODS}", mods);
    }

    private void PreflightExecutable(string exeFullPath)
    {
        var jf = new JFile(exeFullPath);

        // Quick existence check
        if (!jf.Exists())
        {
            _logger.LogError("Executable not found: {Path}", exeFullPath);
        }

        // Best-effort: ensure exec bit (may be ignored if FS is read-only)
        if (!jf.CanExecute())
        {
            try
            {
                bool ok = jf.SetExecutable(true, /*ownerOnly*/ false);
                _logger.LogInformation("SetExecutable({Path}) -> {Ok}", exeFullPath, ok);
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "SetExecutable failed for {Path}", exeFullPath);
            }
        }

        // Touch via managed IO to surface obvious issues early (not fatal)
        try
        {
            using var _ = IOFile.OpenRead(exeFullPath);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "OpenRead failed for {Path}", exeFullPath);
        }
    }

    private static string[] BuildArgvVector(string exe, string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return new[] { exe };

        var parts = args.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        var argv = new string[parts.Length + 1];
        argv[0] = exe;
        parts.CopyTo(argv, 1);
        return argv;
    }

    private static void MergeEnv(string key, string prependDir)
    {
        var old = System.Environment.GetEnvironmentVariable(key);
        string val = string.IsNullOrEmpty(old) ? prependDir : $"{prependDir}:{old}";
        System.Environment.SetEnvironmentVariable(key, val);
    }

    private static void SetEnvIfPresent(string key, string value)
    {
        System.Environment.SetEnvironmentVariable(key, value);
    }
}
#endif
