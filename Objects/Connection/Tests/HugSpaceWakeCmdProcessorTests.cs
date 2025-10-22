using System.Reflection;
using System.Threading.Tasks;
using Moq;
using NetworkMonitor.Connection;
using PuppeteerSharp;
using Xunit;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class HugSpaceWakeCmdProcessorTests
    {
        private static Task<bool> InvokeIsSpaceRunningAsync(IPage page)
        {
            var method = typeof(HugSpaceWakeCmdProcessor).GetMethod(
                "IsSpaceRunning",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var task = (Task<bool>?)method!.Invoke(null, new object[] { page });
            Assert.NotNull(task);
            return task!;
        }

        [Fact]
        public async Task IsSpaceRunning_ReturnsFalse_ForErrorPage()
        {
            var page = new Mock<IPage>(MockBehavior.Strict);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("ErrorPage"))))
                .ReturnsAsync(true);

            var result = await InvokeIsSpaceRunningAsync(page.Object);

            Assert.False(result);
            page.VerifyAll();
        }

        [Fact]
        public async Task IsSpaceRunning_ReturnsTrue_WhenIframePresent()
        {
            var page = new Mock<IPage>(MockBehavior.Strict);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("ErrorPage"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains(".hf.space"))))
                .ReturnsAsync(true);

            var result = await InvokeIsSpaceRunningAsync(page.Object);

            Assert.True(result);
            page.VerifyAll();
        }

        [Fact]
        public async Task IsSpaceRunning_ReturnsTrue_WhenRuntimeStageRunning()
        {
            var page = new Mock<IPage>(MockBehavior.Strict);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("ErrorPage"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains(".hf.space"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<string?>(It.Is<string>(expr => expr.Contains("SVELTE_HYDRATER"))))
                .ReturnsAsync("RUNNING");

            var result = await InvokeIsSpaceRunningAsync(page.Object);

            Assert.True(result);
            page.VerifyAll();
        }

        [Fact]
        public async Task IsSpaceRunning_ReturnsFalse_WhenRuntimeStageError()
        {
            var page = new Mock<IPage>(MockBehavior.Strict);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("ErrorPage"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains(".hf.space"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<string?>(It.Is<string>(expr => expr.Contains("SVELTE_HYDRATER"))))
                .ReturnsAsync("ERROR");

            var result = await InvokeIsSpaceRunningAsync(page.Object);

            Assert.False(result);
            page.VerifyAll();
        }

        [Fact]
        public async Task IsSpaceRunning_ReturnsTrue_WhenRunningBadgePresent()
        {
            var page = new Mock<IPage>(MockBehavior.Strict);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("ErrorPage"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains(".hf.space"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<string?>(It.Is<string>(expr => expr.Contains("SVELTE_HYDRATER"))))
                .ReturnsAsync((string?)null);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("Sleeping"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("form[action$=\"/start\"]"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("Running"))))
                .ReturnsAsync(true);

            var result = await InvokeIsSpaceRunningAsync(page.Object);

            Assert.True(result);
            page.VerifyAll();
        }

        [Fact]
        public async Task IsSpaceRunning_ReturnsFalse_WhenNoPositiveSignals()
        {
            var page = new Mock<IPage>(MockBehavior.Strict);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("ErrorPage"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains(".hf.space"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<string?>(It.Is<string>(expr => expr.Contains("SVELTE_HYDRATER"))))
                .ReturnsAsync((string?)null);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("Sleeping"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("form[action$=\"/start\"]"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("Running"))))
                .ReturnsAsync(false);
            page
                .Setup(p => p.EvaluateExpressionAsync<bool>(It.Is<string>(expr => expr.Contains("SpacePage"))))
                .ReturnsAsync(true);

            var result = await InvokeIsSpaceRunningAsync(page.Object);

            Assert.False(result);
            page.VerifyAll();
        }
    }
}
