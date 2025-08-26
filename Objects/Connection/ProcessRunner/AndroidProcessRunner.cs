#if ANDROID
using Android.App;
using Java.IO;
using Java.Lang;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

            // Prepare environment variables
            var env = new List<string>();
            if (envVars != null)
                env.AddRange(envVars.Select(kv => $"{kv.Key}={kv.Value}"));

            // Always set LD_LIBRARY_PATH to working directory
            env.Add("LD_LIBRARY_PATH=" + workingDirectory);

            // Execute process
            var runtime = Java.Lang.Runtime.GetRuntime();
            var process = runtime.Exec(cmd, env.ToArray(), new Java.IO.File(workingDirectory));

            // Cancelation token handling
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            cts.Token.Register(() =>
            {
                try { process.Destroy(); } catch { }
            });

            // Read stdout asynchronously
            var stdoutTask = Task.Run(() =>
            {
                using var reader = new BufferedReader(new InputStreamReader(process.InputStream));
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    output.AppendLine(line);
                }
            }, token);

            // Read stderr asynchronously
            var stderrTask = Task.Run(() =>
            {
                using var reader = new BufferedReader(new InputStreamReader(process.ErrorStream));
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    token.ThrowIfCancellationRequested();
                    error.AppendLine(line);
                }
            }, token);

            await Task.WhenAll(stdoutTask, stderrTask);
            process.WaitFor();
        }
        catch (Java.IO.IOException ex)
        {
            _logger.LogError(ex, "Failed to start Android process: {Path}", executablePath);
            return ex.Message;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Android process canceled: {Path}", executablePath);
            return "Process canceled or timeout reached.";
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Android process failed: {Path}", executablePath);
            return ex.Message;
        }

        return $"{error} : {output}";
    }
}
#endif
