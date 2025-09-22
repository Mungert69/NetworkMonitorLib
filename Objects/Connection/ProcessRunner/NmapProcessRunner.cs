using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging;
using NetworkMonitor.Utils.Helpers;

namespace NetworkMonitor.Connection
{
    using System.IO;

    public class NmapEnvironment
    {
        public string ExePath { get; }
        public string WorkingDirectory { get; }
        public string DataDir { get; }
        public string NmapPath { get; }
        public string LibDir { get; }

        private readonly ILogger _logger;

        public NmapEnvironment(NetConnectConfig config, ILogger logger)
        {
            _logger = logger;

            ExePath = config.CommandPath;
            WorkingDirectory = config.CommandPath;
            DataDir = $" --datadir {config.CommandPath}";
            NmapPath = Path.Combine(ExePath, "nmap");
            LibDir = config.OqsProviderPath;

            if (!string.IsNullOrEmpty(config.NativeLibDir))
            {
                // Android / special case
                ExePath = config.NativeLibDir;
                WorkingDirectory = config.CommandPath;
                LibraryHelper.SetLDLibraryPath(config.NativeLibDir);

                NmapPath = Path.Combine(config.NativeLibDir, "libnmap_exec.so");
                LibDir = config.NativeLibDir;
            }

            _logger.LogDebug("NmapEnvironment initialized:");
            _logger.LogDebug("  ExePath={exe}", ExePath);
            _logger.LogDebug("  WorkingDir={dir}", WorkingDirectory);
            _logger.LogDebug("  NmapPath={nmap}", NmapPath);
            _logger.LogDebug("  LibDir={lib}", LibDir);
            _logger.LogDebug("  DataDir={data}", DataDir);
        }

        public NmapProcessRunner CreateRunner(ILogger logger)
        {
            return new NmapProcessRunner(WorkingDirectory, ExePath, LibDir, logger);
        }
    }

    public class NmapProcessRunner
    {
        private readonly string _workingDirectory;
        private readonly string _commandPath;
        private readonly string _libDir;
        private readonly ILogger _logger;

        public NmapProcessRunner(string workingDirectory, string commandPath, string libDir, ILogger logger)
        {
            _workingDirectory = workingDirectory;
            _commandPath = commandPath;
            _libDir = libDir;
            _logger = logger;
        }

        public async Task<(bool Success, string Output)> RunAsync(
            string exePath,
            string arguments,
            CancellationToken cancellationToken)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            _logger.LogInformation("🔍 NmapProcessRunner starting");
            _logger.LogInformation("  Executable: {exe}", exePath);
            _logger.LogInformation("  Arguments: {args}", arguments);
            _logger.LogInformation("  WorkingDir: {dir}", _workingDirectory);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _workingDirectory,
                }
            };

            // 🔑 Inject LD_PRELOAD if fakeuid.so exists
            string preloadPath = Path.Combine(_libDir, "fakeuid.so");
            if (File.Exists(preloadPath))
            {
                process.StartInfo.EnvironmentVariables["LD_PRELOAD"] = preloadPath;
                _logger.LogInformation("✅ Injecting LD_PRELOAD={preload}", preloadPath);
            }
            else
            {
                _logger.LogInformation($"ℹ️ No fakeuid.so found at {_libDir}, running without LD_PRELOAD");
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            var combinedOutput = outputBuilder.ToString();
            if (errorBuilder.Length > 0)
                combinedOutput += "\n[stderr]\n" + errorBuilder;
            return (process.ExitCode == 0, combinedOutput);
        }

        // New method for XML output
        public async Task<(bool Success, string StdOut, string StdErr)> RunXmlAsync(
            string exePath,
            string arguments,
            CancellationToken cancellationToken)
        {
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            _logger.LogInformation("🔍 NmapProcessRunner starting for XML output");
            _logger.LogInformation("  Executable: {exe}", exePath);
            _logger.LogInformation("  Arguments: {args}", arguments);
            _logger.LogInformation("  WorkingDir: {dir}", _workingDirectory);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _workingDirectory,
                }
            };

            // 🔑 Inject LD_PRELOAD if fakeuid.so exists
            string preloadPath = Path.Combine(_libDir, "fakeuid.so");
            if (File.Exists(preloadPath))
            {
                process.StartInfo.EnvironmentVariables["LD_PRELOAD"] = preloadPath;
                _logger.LogInformation("✅ Injecting LD_PRELOAD={preload}", preloadPath);
            }
            else
            {
                _logger.LogInformation($"ℹ️ No fakeuid.so found at {_libDir}, running without LD_PRELOAD");
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            return (process.ExitCode == 0, outputBuilder.ToString(), errorBuilder.ToString());
        }
    }
}
