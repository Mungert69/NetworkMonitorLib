// AndroidProcWrapperRunner.cs
#if ANDROID
using Android.App;
using Android.OS;
using Android.Content.PM;
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
    private readonly bool _useLegacyShellWrap;

    // Heuristic flag: are native libs extracted to filesystem?
    // True  -> files should exist under _nativeDir/lib<abi> and be chmod-able
    // False -> libs are loaded in-place from split APKs; _nativeDir may be empty
    private bool _extractionEnabled;

    public AndroidProcWrapperRunner(ILogger logger, string? nativeDir = null)
    {
        _logger = logger;
        _nativeDir = string.IsNullOrWhiteSpace(nativeDir)
            ? Application.Context.ApplicationInfo!.NativeLibraryDir!
            : nativeDir!;
        _useLegacyShellWrap = (int)Build.VERSION.SdkInt <= (int)BuildVersionCodes.M;
    }

    public async Task<string> RunAsync(
        string executablePath,
        string arguments,
        string workingDirectory,
        IDictionary<string, string>? envVars,
        CancellationToken token)
    {
        // Resolve target
        string exeFullPath = System.IO.Path.IsPathRooted(executablePath)
            ? executablePath
            : System.IO.Path.Combine(_nativeDir, executablePath);

        // Detect + log install mode up front
        DetectInstallMode();
        DumpNativeSetup(exeFullPath);

        // Env for dlopen() lookup order (always harmless)
        MergeEnv("LD_LIBRARY_PATH", _nativeDir);
        SetEnvIfPresent("OPENSSL_MODULES", _nativeDir);
        SetEnvIfPresent("LUA_CPATH", $"{_nativeDir}/lib?_lua.so;{_nativeDir}/lib?.so;;");


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

        // Only do path-based file checks if libs are actually extracted
        if (_extractionEnabled)
        {
            PreflightExecutable(exeFullPath);
        }
        else
        {
            _logger.LogInformation("Skip PreflightExecutable: native libs are loaded in-place (not extracted).");
        }

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
        ps.OnExited     += ec   => _logger.LogInformation("Process exited with code {Code}", ec);

        using var ctr = token.Register(() => { try { ps.Stop(); } catch { } });

        string launchPath = exeFullPath;
        string[] launchArgv = NetworkMonitor.Utils.Argv.Tokenize(arguments);

        if (_useLegacyShellWrap && exeFullPath.EndsWith(".so", System.StringComparison.OrdinalIgnoreCase))
        {
            launchPath = "/system/bin/sh";
            var shellCommand = BuildLegacyShellCommand(exeFullPath, launchArgv);
            launchArgv = new[] { "-lc", shellCommand };
            _logger.LogInformation("Legacy shell wrap enabled: sh -lc {Cmd}", shellCommand);
        }

        _logger.LogInformation("Android(procwrapper) exec: {Exe} {Args}", launchPath, NetworkMonitor.Utils.Argv.ForLog(launchArgv));
        if (!ps.Start(launchPath, launchArgv))
        {
            _logger.LogError("Failed to start process: {Path}", launchPath);
            return $"Failed to start: {launchPath}";
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

    /// <summary>
    /// Best-effort detection of native lib install mode.
    /// </summary>
    private void DetectInstallMode()
    {
        bool anyFiles = false;
        try
        {
            var files = new JFile(_nativeDir).ListFiles();
            anyFiles = files is { Length: > 0 } && files.Any(f => f.CanRead());
        }
        catch { /* ignore */ }

        // If directory has readable entries, assume extraction is enabled.
        // Otherwise, assume Play-style in-place loading from APK splits.
        _extractionEnabled = anyFiles;

        // Bonus: log manifest hint if available (SDK 24+ has sourceDir/publicSourceDir)
        try
        {
            var pm = Application.Context.PackageManager!;
            var ai = pm.GetApplicationInfo(Application.Context.PackageName!,
                                           PackageInfoFlags.MetaData);
            _logger.LogInformation("Install sourceDir={Src} publicSourceDir={Pub}",
                ai.SourceDir, ai.PublicSourceDir);
        }
        catch { /* best-effort only */ }

        _logger.LogInformation("Native lib install mode: {Mode} (NativeLibraryDir={Dir})",
            _extractionEnabled ? "Extracted to filesystem" : "In-place from APK (not extracted)",
            _nativeDir);
    }

    private void DumpNativeSetup(string exeFullPath)
    {
        _logger.LogInformation("NativeLibraryDir = {Dir}", _nativeDir);
        _logger.LogInformation("Build.SUPPORTED_ABIS = {Abis}", string.Join(",", Build.SupportedAbis ?? new string[0]));
        _logger.LogInformation("Exec candidate = {Exe}", exeFullPath);

        if (_extractionEnabled)
        {
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
        }
        else
        {
            _logger.LogInformation("Libraries are not extracted; directory enumeration is expected to be empty.");
        }

        string[] expected =
        {
            "libprocwrapper.so",
            "libopenssl_exec.so",
            "libbusybox_exec.so",
            "libnmap_exec.so",
            "libssl.so",
            "libcrypto.so",
            "libc++_shared.so",
            "liboqsprovider.so",
            "libopenssl_lua.so" 
        };

        if (_extractionEnabled)
        {
            foreach (var name in expected.Distinct())
            {
                var p  = System.IO.Path.Combine(_nativeDir, name);
                var jf = new JFile(p);
                _logger.LogInformation("Check {Name}: exists={E} size={S} R={R} X={X}",
                    name, jf.Exists(), jf.Length(), jf.CanRead(), jf.CanExecute());
            }
        }

        var ldlp = System.Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "(null)";
        var mods = System.Environment.GetEnvironmentVariable("OPENSSL_MODULES") ?? "(null)";
        _logger.LogInformation("LD_LIBRARY_PATH = {LD}", ldlp);
        _logger.LogInformation("OPENSSL_MODULES = {MODS}", mods);

        // Useful hint when not extracted
        if (!_extractionEnabled)
        {
            _logger.LogInformation("Hint: libs will still load via dlopen(System.loadLibrary) by name.");
            _logger.LogInformation("      If you require on-disk files, enable extraction via BundleConfig.json (uncompress_native_libraries).");
        }
    }

    private void PreflightExecutable(string exeFullPath)
    {
        var jf = new JFile(exeFullPath);

        if (!jf.Exists())
            _logger.LogError("Executable not found: {Path}", exeFullPath);

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

        try
        {
            using var _ = IOFile.OpenRead(exeFullPath);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "OpenRead failed for {Path}", exeFullPath);
        }
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

    private string BuildLegacyShellCommand(string exeFullPath, string[] argumentVector)
    {
        var sb = new StringBuilder();
        bool first = true;

        void AppendWithSpace(string segment)
        {
            if (!first)
                sb.Append(' ');
            sb.Append(segment);
            first = false;
        }

        if (!string.IsNullOrEmpty(_nativeDir))
        {
            AppendWithSpace($"LD_LIBRARY_PATH={ShellQuote(_nativeDir)}");
            AppendWithSpace($"OPENSSL_MODULES={ShellQuote(_nativeDir)}");
        }

        AppendWithSpace(ShellQuote(exeFullPath));
        foreach (var arg in argumentVector)
        {
            AppendWithSpace(ShellQuote(arg));
        }

        return sb.ToString();
    }

    private static string ShellQuote(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "''";

        var safe = value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/' or ':' or '@');
        if (safe)
            return value;

        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }

    // (BuildArgvVector inlined into ps.Start() call above; keep if you prefer)

}
#endif
