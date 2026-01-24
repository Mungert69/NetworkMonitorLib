using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using Xunit;

namespace NetworkMonitor.Connection.CommandProcessors.Tests
{
    public class QuantumPortScannerCmdProcessorTests
    {
        [Fact]
        public void GetCommandHelp_DescribesTimeoutBehaviorAndDefaults()
        {
            var logger = new Mock<ILogger>();
            var cmdStates = new Mock<ILocalCmdProcessorStates>();
            var rabbitRepo = new Mock<IRabbitRepo>();
            var configMock = new Mock<IConfiguration>();
            var netConfig = new NetConnectConfig(configMock.Object, "/bin/");
            netConfig.OqsProviderPath = FindOqsProviderPath();

            using var processor = new QuantumPortScannerCmdProcessor(
                logger.Object,
                cmdStates.Object,
                rabbitRepo.Object,
                netConfig);

            var help = processor.GetCommandHelp();

            Assert.Contains("Overall scan timeout is derived from per-port timeout and parallelism", help);
            Assert.Contains("-T4 --open --max-retries 2 --host-timeout 30s --initial-rtt-timeout 200ms --max-rtt-timeout 1s", help);
        }

        private static string FindOqsProviderPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "NetworkMonitorService");
                var algoTable = Path.Combine(candidate, "AlgoTable.csv");
                var curves = Path.Combine(candidate, "curves");
                if (File.Exists(algoTable) && File.Exists(curves))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not locate AlgoTable.csv and curves for quantum tests.");
        }
    }
}
