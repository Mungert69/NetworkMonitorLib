using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests
{
    public class PlatformAwareCmdProcessorTests
    {
        private static NetConnectConfig CreateConfig(string osPlatform, string? commandPath = null)
        {
            var configuration = new ConfigurationBuilder().Build();
            var config = new NetConnectConfig(configuration, "TestSection");
            config.OSPlatform = osPlatform;
            if (!string.IsNullOrWhiteSpace(commandPath))
            {
                config.CommandPath = commandPath;
            }

            return config;
        }

        [Fact]
        public async Task BleBroadcastCmdProcessor_WhenOsPlatformIsWindowsOnNonWindowsBuild_ReturnsPlatformMessage()
        {
            var logger = new Mock<ILogger>();
            var rabbitRepo = new Mock<IRabbitRepo>();
            var cmdState = new LocalCmdProcessorStates("blebroadcast", "BLE Broadcast") { IsCmdAvailable = true };
            var config = CreateConfig("windows");
            var sut = new BleBroadcastCmdProcessor(logger.Object, cmdState, rabbitRepo.Object, config);

            try
            {
                var result = await sut.RunCommand("--address AA:BB:CC:DD:EE:FF", CancellationToken.None);

                Assert.False(result.Success);
                Assert.Contains("only available on Android or Windows builds", result.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                sut.Dispose();
            }
        }

        [Fact]
        public async Task BleBroadcastListenCmdProcessor_WhenOsPlatformIsWindowsOnNonWindowsBuild_ReturnsPlatformMessage()
        {
            var logger = new Mock<ILogger>();
            var rabbitRepo = new Mock<IRabbitRepo>();
            var cmdState = new LocalCmdProcessorStates("blebroadcastlisten", "BLE Broadcast Listen") { IsCmdAvailable = true };
            var config = CreateConfig("windows");
            var sut = new BleBroadcastListenCmdProcessor(logger.Object, cmdState, rabbitRepo.Object, config);

            try
            {
                var result = await sut.RunCommand("", CancellationToken.None);

                Assert.False(result.Success);
                Assert.Contains("only available on Android or Windows builds", result.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                sut.Dispose();
            }
        }

        [Fact]
        public void CameraCaptureCmdProcessor_ResolveFfmpegPath_UsesNetConfigOsPlatformForWindowsExecutableName()
        {
            var logger = new Mock<ILogger>();
            var rabbitRepo = new Mock<IRabbitRepo>();
            var cmdState = new LocalCmdProcessorStates("cameracapture", "Camera Capture") { IsCmdAvailable = true };
            var tempDir = Path.Combine(Path.GetTempPath(), "nm-camera-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var windowsExePath = Path.Combine(tempDir, "ffmpeg.exe");
            File.WriteAllText(windowsExePath, "stub");

            var config = CreateConfig("windows", tempDir + Path.DirectorySeparatorChar);
            var sut = new CameraCaptureCmdProcessor(logger.Object, cmdState, rabbitRepo.Object, config);

            try
            {
                var method = typeof(CameraCaptureCmdProcessor).GetMethod("ResolveFfmpegPath", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(method);

                var resolved = (string)method!.Invoke(sut, new object[] { "ffmpeg" })!;
                Assert.Equal(windowsExePath, resolved);
            }
            finally
            {
                sut.Dispose();
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                }
            }
        }
    }
}
