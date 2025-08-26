#if ANDROID
using Android.App;
using Java.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace NetworkMonitor.Connection;

public class AndroidProcessRunner : IPlatformProcessRunner
{
    private readonly ILogger _logger;

    public AndroidProcessRunner(ILogger logger) => _logger = logger;

    public async Task<string> RunAsync(
        string executablePath,
        string arguments,
        string workingDirectory,
        IDictionary<string, string>? envVars,
        CancellationToken token)
    {
        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();

        try
        {
            // Build full command
            var argsList = new List<string> { executablePath };
            if (!string.IsNullOrWhiteSpace(arguments))
                argsList.AddRange(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            string cmd = string.Join(' ', argsList);
            _logger.LogInformation("Executing Android command: {Cmd}", cmd);

            // Merge current environment variables
            var mergedEnv = new List<string>();
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                mergedEnv.Add($"{entry.Key}={entry.Value}");

            // Add/override LD_LIBRARY_PATH
            string existingLd = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
            mergedEnv.Add("LD_LIBRARY_PATH=" + workingDirectory +
                          (string.IsNullOrEmpty(existingLd) ? "" : ":" + existingLd));

            // Merge additional envVars
            if (envVars != null)
                mergedEnv.AddRange(envVars.Select(kv => $"{kv.Key}={kv.Value}"));

            _logger.LogDebug("Final environment variables for process:");
            foreach (var e in mergedEnv)
                _logger.LogDebug("  {Env}", e);

            // Execute process
            _logger.LogDebug("Starting process in working directory: {Dir}", workingDirectory);
            var runtime = Java.Lang.Runtime.GetRuntime();
            var process = runtime.Exec(cmd, mergedEnv.ToArray(), new Java.IO.File(workingDirectory));
            _logger.LogInformation("Process started successfully.");

            // Cancellation token handling
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            cts.Token.Register(() =>
            {
                _logger.LogWarning("Cancellation requested. Destroying process...");
                try { process.Destroy(); } catch { }
            });

            // Read stdout asynchronously
            var stdoutTask = Task.Run(() =>
            {
                try
                {
                    using var reader = new BufferedReader(new InputStreamReader(process.InputStream));
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        token.ThrowIfCancellationRequested();
                        output.AppendLine(line);
                        _logger.LogInformation("[STDOUT] {Line}", line);
                    }
                }
                catch (Java.IO.InterruptedIOException)
                {
                    _logger.LogInformation("STDOUT read interrupted by cancellation.");
                }
            }, token);

            // Read stderr asynchronously
            var stderrTask = Task.Run(() =>
            {
                try
                {
                    using var reader = new BufferedReader(new InputStreamReader(process.ErrorStream));
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        token.ThrowIfCancellationRequested();
                        error.AppendLine(line);
                        _logger.LogInformation("[STDERR] {Line}", line);
                    }
                }
                catch (Java.IO.InterruptedIOException)
                {
                    _logger.LogInformation("STDERR read interrupted by cancellation.");
                }
            }, token);

            await Task.WhenAll(stdoutTask, stderrTask);

            int exitCode = process.WaitFor();
            _logger.LogInformation("Process exited with code: {ExitCode}", exitCode);
        }
        catch (Java.IO.IOException ex)
        {
            _logger.LogError(ex, "Failed to start Android process: {Path}", executablePath);
            return ex.Message;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Android process canceled: {Path}", executablePath);
            return "Process canceled or timeout reached.";
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Android process failed: {Path}", executablePath);
            return ex.Message;
        }

        _logger.LogInformation("Process finished. Returning combined output.");
        return $"{error} : {output}";
    }
}
#endif
