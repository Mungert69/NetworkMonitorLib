using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace NetworkMonitor.Connection
{
    /// <summary>
    /// Centralized helper for launching Puppeteer, preparing pages, cache-busting navigation,
    /// resolving HF app origins, and lightweight activity primitives (queue sniff, linger, etc).
    /// </summary>
    public static class WebAutomationHelper
    {
        public sealed class BrowserSessionOptions
        {
            public ViewPortOptions? Viewport { get; set; } = new() { Width = 1280, Height = 800 };
            public string? UserAgent { get; set; } =
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36";
            public Dictionary<string, string>? ExtraHeaders { get; set; } = new()
            {
                ["Cache-Control"] = "no-cache, no-store, must-revalidate",
                ["Pragma"]        = "no-cache",
                ["Expires"]       = "0",
                ["Accept-Language"] = "en-US,en;q=0.9"
            };
            public bool ApplyStealth { get; set; } = true;
        }

        public sealed class BrowserSession : IAsyncDisposable, IDisposable
        {
            public IBrowser Browser { get; }
            public IPage Page { get; }

            public BrowserSession(IBrowser browser, IPage page)
            {
                Browser = browser;
                Page = page;
            }

            public void Dispose()
            {
                try { Page?.Dispose(); } catch { }
                try { Browser?.Dispose(); } catch { }
            }

            public async ValueTask DisposeAsync()
            {
                try { await Page.CloseAsync(); } catch { }
                try { await Browser.CloseAsync(); } catch { }
            }
        }

        /// <summary>
        /// Launch Chromium via ILaunchHelper, open a page, set viewport, headers, UA, and optional stealth.
        /// </summary>
        public static async Task<BrowserSession> OpenSessionAsync(
            ILaunchHelper launchHelper,
            NetConnectConfig netConfig,
            ILogger logger,
            CancellationToken ct,
            int defaultPageTimeoutMs = 10_000,
            BrowserSessionOptions? options = null)
        {
            options ??= new BrowserSessionOptions();

            bool headless = launchHelper.CheckDisplay(logger, netConfig.ForceHeadless);
            var launchOptions = await launchHelper.GetLauncher(netConfig.CommandPath, logger, headless);

            var browser = await Puppeteer.LaunchAsync(launchOptions);
            var page = await browser.NewPageAsync();
            page.DefaultTimeout = defaultPageTimeoutMs;

            if (options.Viewport is not null)
                await page.SetViewportAsync(options.Viewport);

            if (!string.IsNullOrWhiteSpace(options.UserAgent))
                await page.SetUserAgentAsync(options.UserAgent);

            if (options.ExtraHeaders is not null && options.ExtraHeaders.Count > 0)
                await page.SetExtraHttpHeadersAsync(options.ExtraHeaders);

            if (options.ApplyStealth)
                await ApplyStealthAsync(page);

            return new BrowserSession(browser, page);
        }

        /// <summary>Light stealth shim: removes webdriver, sets languages/plugins/screen & nudges chrome object.</summary>
        public static async Task ApplyStealthAsync(IPage page)
        {
            await page.EvaluateFunctionOnNewDocumentAsync(@"
                () => {
                    Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                    Object.defineProperty(navigator, 'plugins',  { get: () => [1,2,3] });
                    Object.defineProperty(navigator, 'languages',{ get: () => ['en-US','en'] });
                    Object.defineProperty(screen, 'width',  { get: () => 1920 });
                    Object.defineProperty(screen, 'height', { get: () => 1080 });
                    window.chrome = { runtime: {}, loadTimes: () => ({}), csi: () => ({}) };

                    const originalQuery = window.navigator.permissions.query;
                    window.navigator.permissions.query = (parameters) => (
                        parameters.name === 'notifications'
                          ? Promise.resolve({ state: Notification.permission })
                          : originalQuery(parameters)
                    );
                }
            ");
        }

        /// <summary>Resolve the live app origin for a Hugging Face wrapper URL. If already an app URL, returns input.</summary>
        public static async Task<string> ResolveHuggingFaceAppOriginAsync(
            IPage page,
            string inputUrl,
            ILogger logger,
            CancellationToken ct)
        {
            static bool IsWrapperUrl(string u) =>
                !string.IsNullOrWhiteSpace(u) &&
                u.Contains("huggingface.co/spaces/", StringComparison.OrdinalIgnoreCase);

            if (!IsWrapperUrl(inputUrl)) return inputUrl;

            await page.GoToAsync(inputUrl, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
            });

            var appUrl = await page.EvaluateExpressionAsync<string>(@"
                (function () {
                  try {
                    const hydraters = [...document.querySelectorAll('.SVELTE_HYDRATER[data-target=""SpacePageInner""]')];
                    for (const el of hydraters) {
                      const props = el.getAttribute('data-props');
                      if (props) {
                        try {
                          const obj = JSON.parse(props);
                          if (obj && obj.space && obj.space.iframe && obj.space.iframe.src) return obj.space.iframe.src;
                          if (obj && obj.iframeSrc) return obj.iframeSrc;
                        } catch {}
                      }
                    }
                  } catch {}
                  const ifr = document.querySelector('iframe[src*="".hf.space""]');
                  if (ifr) return ifr.getAttribute('src');
                  return '';
                })()
            ") ?? "";

            if (!string.IsNullOrWhiteSpace(appUrl))
                return appUrl;

            var sub = await page.EvaluateExpressionAsync<string>(@"
                (function(){
                  const t = document.body ? document.body.innerHTML : '';
                  const m = t.match(/https?:\/\/([a-z0-9-]+)\.hf\.space/ig);
                  return m && m[0] ? m[0] : '';
                })()
            ") ?? "";

            if (!string.IsNullOrWhiteSpace(sub))
                return sub;

            logger.LogInformation($"Could not auto-resolve app origin; using the input URL {inputUrl} as-is.");
            return inputUrl;
        }

        /// <summary>Navigate with a cache-buster query param to force origin hits.</summary>
        public static async Task GoToWithCacheBusterAsync(
            IPage page,
            string url,
            string cacheParamName = "keepalive",
            NavigationOptions? nav = null)
        {
            var nonce = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var urlWithNonce = url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?")
                               + cacheParamName + "=" + nonce;

            await page.GoToAsync(urlWithNonce, nav ?? new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
            });
        }

        /// <summary>Fire an extra no-store fetch to guarantee one more server hit (handy for static sites).</summary>
        public static async Task FireNoStoreFetchAsync(IPage page, string path = "/")
        {
            try
            {
                await page.EvaluateExpressionAsync(
                    $"fetch('{path}', {{ cache: 'no-store', headers: {{ 'Cache-Control': 'no-store' }} }})");
            }
            catch { /* ignore */ }
        }

        /// <summary>Wait until we see Gradio queue requests OR a max delay elapses.</summary>
        public static async Task<bool> WaitForGradioQueueOrDelayAsync(
            IPage page, TimeSpan maxDelay, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(object? _, RequestEventArgs e)
            {
                try
                {
                    var u = e.Request.Url ?? "";
                    if (u.Contains("/queue/join", StringComparison.OrdinalIgnoreCase) ||
                        u.Contains("/queue/data", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!tcs.Task.IsCompleted) tcs.TrySetResult(true);
                    }
                }
                catch { }
            }

            page.Request += Handler;
            try
            {
                var delayTask = Task.Delay(maxDelay, ct);
                var first = await Task.WhenAny(tcs.Task, delayTask);
                return first == tcs.Task && tcs.Task.Result;
            }
            finally
            {
                page.Request -= Handler;
            }
        }

        public static async Task<bool> WaitForNetworkIdleSafeAsync(IPage page, int idleMs, int timeoutMs)
        {
            try
            {
                await page.WaitForNetworkIdleAsync(new() { IdleTime = idleMs, Timeout = timeoutMs });
                return true;
            }
            catch { return false; }
        }
    }
}
