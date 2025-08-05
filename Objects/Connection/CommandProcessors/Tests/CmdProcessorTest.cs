using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using NetworkMonitor.Objects;
using NetworkMonitor.Objects.Repository;
using NetworkMonitor.Objects.ServiceMessage;
using NetworkMonitor.Connection;

namespace NetworkMonitor.Connection.CommandProcessors.Tests
{
    public class CmdProcessorTest
    {
        private class TestCmdProcessor : CmdProcessor
        {
            public TestCmdProcessor(
                ILogger logger,
                ILocalCmdProcessorStates cmdProcessorStates,
                IRabbitRepo rabbitRepo,
                NetConnectConfig netConfig)
                : base(logger, cmdProcessorStates, rabbitRepo, netConfig) { }

            public new Dictionary<string, string> ParseArguments(string arguments) => base.ParseArguments(arguments);
            public new Task<string> SendMessage(string output, ProcessorScanDataObj? processorScanDataObj) => base.SendMessage(output, processorScanDataObj);
        }

        private (TestCmdProcessor, Mock<ILocalCmdProcessorStates>, Mock<IRabbitRepo>, NetConnectConfig, Mock<ILogger>) CreateProcessor()
        {
            var logger = new Mock<ILogger>();
            var cmdStates = new Mock<ILocalCmdProcessorStates>();
            var rabbitRepo = new Mock<IRabbitRepo>();
            // Provide required constructor args for NetConnectConfig
            var configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
            var netConfig = new NetConnectConfig(configMock.Object, "/bin/")
            {
                CmdReturnDataLineLimit = 2
            };
            return (new TestCmdProcessor(logger.Object, cmdStates.Object, rabbitRepo.Object, netConfig), cmdStates, rabbitRepo, netConfig, logger);
        }

        [Theory]
        [InlineData("--foo bar", "foo", "bar")]
        [InlineData("--foo=bar", "foo", "bar")]
        [InlineData("--foo=\"bar baz\"", "foo", "bar baz")]
        [InlineData("--foo \"bar baz\"", "foo", "bar baz")]
        [InlineData("--flag", "flag", "true")]
        [InlineData("--foo=bar --baz=qux", "baz", "qux")]
        [InlineData("--foo=\"bar\" --baz=\"qux quux\"", "baz", "qux quux")]
        [InlineData("--foo", "foo", "true")]
        [InlineData("--foo=\"\"", "foo", "")]
        [InlineData("--foo=bar --foo=baz", "foo", "baz")] // last wins
        public void ParseArguments_ParsesVariousFormats(string input, string key, string expectedValue)
        {
            var (proc, _, _, _, _) = CreateProcessor();
            var result = proc.ParseArguments(input);
            Assert.True(result.ContainsKey(key));
            Assert.Equal(expectedValue, result[key]);
        }

        [Fact]
        public void ParseArguments_ReturnsEmptyDictionaryOnNoMatch()
        {
            var (proc, _, _, _, _) = CreateProcessor();
            var result = proc.ParseArguments("random text without args");
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseArguments_HandlesMultipleArguments()
        {
            var (proc, _, _, _, _) = CreateProcessor();
            var input = "--foo=bar --baz=\"qux quux\" --flag";
            var result = proc.ParseArguments(input);
            Assert.Equal(3, result.Count);
            Assert.Equal("bar", result["foo"]);
            Assert.Equal("qux quux", result["baz"]);
            Assert.Equal("true", result["flag"]);
        }

        [Fact]
        public void ParseArguments_HandlesNoArguments()
        {
            var (proc, _, _, _, _) = CreateProcessor();
            var result = proc.ParseArguments("");
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseArguments_HandlesQuotedValuesWithSpaces()
        {
            var (proc, _, _, _, _) = CreateProcessor();
            var input = "--search_term=\"how to use network analytics to predict network outages\" --target_domain=\"blog.readyforquantum.com\"";
            var result = proc.ParseArguments(input);
            Assert.Equal("how to use network analytics to predict network outages", result["search_term"]);
            Assert.Equal("blog.readyforquantum.com", result["target_domain"]);
        }

        [Fact]
        public void GetCommandHelp_ReturnsDefault()
        {
            var (proc, _, _, _, _) = CreateProcessor();
            Assert.Equal("No help file available", proc.GetCommandHelp());
        }

        [Fact]
        public async Task Scan_LogsWarningAndSendsMessage()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            cmdStates.SetupGet(x => x.CmdName).Returns("testcmd");
            cmdStates.SetupGet(x => x.CmdDisplayName).Returns("Test Command");
            cmdStates.SetupProperty(x => x.IsSuccess);
            cmdStates.SetupProperty(x => x.IsRunning);

            var scanData = new ProcessorScanDataObj { SendMessage = true, MessageID = "1", CallingService = "svc" };
            rabbitRepo.Setup(x => x.PublishAsync<ProcessorScanDataObj>(
                It.IsAny<string>(),
                It.IsAny<ProcessorScanDataObj>(),
                ""))
                .Returns(Task.CompletedTask);

            await proc.Scan();

            Assert.False(cmdStates.Object.IsSuccess);
            Assert.False(cmdStates.Object.IsRunning);
        }

        [Fact]
        public async Task AddServices_LogsWarningAndSendsMessage()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            cmdStates.SetupGet(x => x.CmdName).Returns("testcmd");
            cmdStates.SetupGet(x => x.CmdDisplayName).Returns("Test Command");
            cmdStates.SetupProperty(x => x.IsSuccess);
            cmdStates.SetupProperty(x => x.IsRunning);

            var scanData = new ProcessorScanDataObj { SendMessage = true, MessageID = "1", CallingService = "svc" };
            rabbitRepo.Setup(x => x.PublishAsync<ProcessorScanDataObj>(
                It.IsAny<string>(),
                It.IsAny<ProcessorScanDataObj>(),
                ""))
                .Returns(Task.CompletedTask);

            await proc.AddServices();

            Assert.False(cmdStates.Object.IsSuccess);
            Assert.False(cmdStates.Object.IsRunning);
        }

        [Fact]
        public async Task CancelScan_WhenCmdNotAvailable_SendsWarning()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            cmdStates.SetupGet(x => x.IsCmdAvailable).Returns(false);
            cmdStates.SetupGet(x => x.CmdName).Returns("testcmd");
            cmdStates.SetupGet(x => x.CmdDisplayName).Returns("Test Command");
            cmdStates.SetupProperty(x => x.IsSuccess);
            cmdStates.SetupProperty(x => x.IsRunning);

            await proc.CancelScan();

            Assert.False(cmdStates.Object.IsSuccess);
            Assert.False(cmdStates.Object.IsRunning);
        }

        [Fact]
        public async Task CancelScan_WhenRunning_CancelsToken()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            cmdStates.SetupGet(x => x.IsCmdAvailable).Returns(true);
            cmdStates.SetupGet(x => x.IsRunning).Returns(true);
            cmdStates.SetupGet(x => x.CmdName).Returns("testcmd");
            cmdStates.SetupGet(x => x.CmdDisplayName).Returns("Test Command");
            cmdStates.SetupProperty(x => x.RunningMessage);

            var cts = new CancellationTokenSource();
            typeof(CmdProcessor)
                .GetField("_cancellationTokenSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(proc, cts);

            await proc.CancelScan();

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public async Task CancelScan_WhenNotRunning_LogsInfo()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            cmdStates.SetupGet(x => x.IsCmdAvailable).Returns(true);
            cmdStates.SetupGet(x => x.IsRunning).Returns(false);
            cmdStates.SetupGet(x => x.CmdName).Returns("testcmd");
            cmdStates.SetupGet(x => x.CmdDisplayName).Returns("Test Command");
            cmdStates.SetupProperty(x => x.CompletedMessage);

            await proc.CancelScan();

            Assert.Contains("No", cmdStates.Object.CompletedMessage);
        }

        [Fact]
        public async Task CancelCommand_ReturnsSuccessIfFound()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource();
            var runningTask = Task.Run(() => tcs.Task);

            var commandTask = new CommandTask("msgid", async () => await Task.Delay(1), cts)
            {
                RunningTask = runningTask
            };

            // Add to _runningTasks via reflection
            var runningTasks = (System.Collections.Concurrent.ConcurrentDictionary<string, CommandTask>)
                typeof(CmdProcessor)
                .GetField("_runningTasks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(proc);
            runningTasks.TryAdd("msgid", commandTask);

            var result = await proc.CancelCommand("msgid");
            Assert.True(result.Success);
            Assert.Contains("cancelled", result.Message);
        }

        [Fact]
        public async Task CancelCommand_ReturnsWarningIfNotFound()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            var result = await proc.CancelCommand("notfound");
            Assert.False(result.Success);
            Assert.Contains("no running command", result.Message);
        }

        [Fact]
        public async Task RunCommand_WhenCmdNotAvailable_ReturnsError()
        {
            var (proc, cmdStates, rabbitRepo, netConfig, logger) = CreateProcessor();
            cmdStates.SetupGet(x => x.IsCmdAvailable).Returns(false);
            cmdStates.SetupGet(x => x.CmdDisplayName).Returns("Test Command");

            var result = await proc.RunCommand("echo test", CancellationToken.None, null);
            Assert.False(result.Success);
            Assert.Contains("not available", result.Message);
        }

        [Fact]
        public async Task PublishCommandHelp_PublishesHelp()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            var scanData = new ProcessorScanDataObj
            {
                MessageID = "helpid",
                CallingService = "svc"
            };
            rabbitRepo.Setup(x => x.PublishAsync<ProcessorScanDataObj>(
                It.IsAny<string>(),
                It.IsAny<ProcessorScanDataObj>(),
                ""))
                .Returns(Task.CompletedTask)
                .Verifiable();

            var result = await proc.PublishCommandHelp(scanData);
            Assert.True(result.Success);
            Assert.Contains("published help message", result.Message);
            rabbitRepo.Verify();
        }

        [Fact]
        public async Task PublishCommandHelp_ReturnsErrorOnException()
        {
            var (proc, cmdStates, rabbitRepo, _, logger) = CreateProcessor();
            var scanData = new ProcessorScanDataObj
            {
                MessageID = "helpid",
                CallingService = "svc"
            };
            rabbitRepo.Setup(x => x.PublishAsync<ProcessorScanDataObj>(
                It.IsAny<string>(),
                It.IsAny<ProcessorScanDataObj>(),
                ""))
                .Throws(new Exception("fail"));

            var result = await proc.PublishCommandHelp(scanData);
            Assert.False(result.Success);
            Assert.Contains("could not publish help", result.Message);
        }

        [Fact]
        public async Task SendMessage_PaginatesOutput()
        {
            var (proc, cmdStates, rabbitRepo, netConfig, logger) = CreateProcessor();
            var scanData = new ProcessorScanDataObj
            {
                SendMessage = true,
                LineLimit = 2,
                Page = 1,
                MessageID = "msgid",
                CallingService = "svc"
            };
            rabbitRepo.Setup(x => x.PublishAsync<ProcessorScanDataObj>(It.IsAny<string>(), It.IsAny<ProcessorScanDataObj>(),""))
                .Returns(Task.CompletedTask);

            var output = "line1\nline2\nline3\nline4";
            var result = await proc.SendMessage(output, scanData);

            Assert.Contains("line1", result);
            Assert.Contains("line2", result);
            Assert.Contains("Showing page", result);
        }

        [Fact]
        public async Task SendMessage_ReturnsOriginalIfNoScanDataObj()
        {
            var (proc, _, _, _, _) = CreateProcessor();
            var output = "test output";
            var result = await proc.SendMessage(output, null);
            Assert.Equal(output, result);
        }

        [Fact]
        public async Task CancelRun_WhenRunning_CancelsToken()
        {
            var (proc, cmdStates, _, _, _) = CreateProcessor();
            cmdStates.SetupGet(x => x.IsCmdRunning).Returns(true);
            cmdStates.SetupGet(x => x.CmdName).Returns("testcmd");
            cmdStates.SetupGet(x => x.CmdDisplayName).Returns("Test Command");
            cmdStates.SetupProperty(x => x.RunningMessage);

            var cts = new CancellationTokenSource();
            typeof(CmdProcessor)
                .GetField("_cancellationTokenSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(proc, cts);

            await proc.CancelRun();

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public async Task CancelRun_WhenNotRunning_AppendsCompletedMessage()
        {
            var (proc, cmdStates, _, _, _) = CreateProcessor();
            cmdStates.SetupGet(x => x.IsCmdRunning).Returns(false);
            cmdStates.SetupGet(x => x.CmdName).Returns("testcmd");
            cmdStates.SetupProperty(x => x.CompletedMessage);

            await proc.CancelRun();

            Assert.Contains("No", cmdStates.Object.CompletedMessage);
        }
    }
}