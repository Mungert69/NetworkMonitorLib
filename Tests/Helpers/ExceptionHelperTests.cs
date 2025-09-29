using System;
using System.IO;
using System.Threading.Tasks;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class ExceptionHelperTests
{
    [Fact]
    public async Task HandleGlobalException_WritesToConsole()
    {
        var originalOut = Console.Out;
        var originalProxy = Environment.GetEnvironmentVariable("https_proxy");
        var writer = new StringWriter();
        Console.SetOut(writer);
        Environment.SetEnvironmentVariable("https_proxy", "http://127.0.0.1:9");

        try
        {
            ExceptionHelper.HandleGlobalException(new InvalidOperationException("boom"), "Test");
            await Task.Delay(200);

            var output = writer.ToString();
            Assert.Contains("Test: boom", output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable("https_proxy", originalProxy);
        }
    }
}
