using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace NetworkMonitor.Connection
{
    public interface IBrowserHost : IAsyncDisposable, IDisposable
    {
        Task<IBrowser> GetBrowserAsync(CancellationToken ct = default);
        Task<T> RunWithPage<T>(Func<IPage, Task<T>> work, CancellationToken ct = default);
    }

    public sealed class BrowserHost : IBrowserHost
    {
        private const int DefaultPageTimeoutMs = 10_000; // safe per-page default

        private readonly ILaunchHelper _launchHelper;
        private readonly NetConnectConfig _netConfig;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _gate;
        private IBrowser? _browser;
        private bool _disposed;

        public BrowserHost(ILaunchHelper launchHelper, NetConnectConfig netConfig, ILogger<BrowserHost> logger, int maxConcurrentPages = 1)
        {
            _launchHelper = launchHelper;
            _netConfig = netConfig;
            _logger = logger;
            _gate = new SemaphoreSlim(Math.Max(1, maxConcurrentPages), Math.Max(1, maxConcurrentPages));
        }

        public async Task<IBrowser> GetBrowserAsync(CancellationToken ct = default)
        {
            // Lazy launch and relaunch-after-crash
            if (_browser == null || _browser.IsClosed)
            {
                bool headless = _launchHelper.CheckDisplay(_logger, _netConfig.ForceHeadless);
                var lo = await _launchHelper.GetLauncher(_netConfig.CommandPath, _logger, headless);
                _browser = await Puppeteer.LaunchAsync(lo);
                _logger.LogInformation("Shared Chromium launched.");
                _browser.Disconnected += (_, __) =>
                {
                    _logger.LogWarning("Shared Chromium disconnected; will relaunch on next request.");
                    try { _browser?.Dispose(); } catch { }
                    _browser = null;
                };
            }
            return _browser;
        }

        public async Task<T> RunWithPage<T>(Func<IPage, Task<T>> work, CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                var browser = await GetBrowserAsync(ct);

                // Per-task page (fallback to default context for broad compatibility)
                var page = await browser.NewPageAsync();
                try
                {
                    // Apply your standard prep via helper (viewport/UA/headers/stealth/timeout)
                    await WebAutomationHelper.PreparePageAsync(
                        page,
                        defaultPageTimeoutMs: DefaultPageTimeoutMs,
                        options: null);

                    return await work(page);
                }
                finally
                {
                    try { await page.CloseAsync(); } catch { /* best effort */ }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RunWithPage failed: {Message}", ex.Message);
                throw;
            }
            finally
            {
                try { _gate.Release(); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _browser?.Dispose(); } catch { }
            _gate.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_browser != null) await _browser.CloseAsync(); } catch { }
            _gate.Dispose();
        }
    }
}
