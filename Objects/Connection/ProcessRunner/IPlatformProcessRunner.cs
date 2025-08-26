
namespace NetworkMonitor.Connection;
public interface IPlatformProcessRunner
{
    Task<string> RunAsync(string executablePath, string arguments, string workingDirectory,
                          IDictionary<string, string>? envVars, CancellationToken token);
}
