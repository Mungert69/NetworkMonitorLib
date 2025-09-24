// SiteHashConnect.cs
using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace NetworkMonitor.Connection
{
    public class SiteHashConnect : NetConnect
    {
        private ILaunchHelper? _launchHelper;
        private readonly string _commandPath;

        public SiteHashConnect(string commandPath, ILaunchHelper? launchHelper = null)
        {
            _commandPath = commandPath;
            _launchHelper = launchHelper;
        }

        public override async Task Connect()
        {
            PreConnect();
            try
            {
                if (_launchHelper == null)
                {
                    ProcessException("PuppeteerSharp browser is missing, check installation", "Puppeteer Missing");
                    return;
                }

                // Build absolute URI with optional port
                UriBuilder targetUri;
                if (Uri.TryCreate(MpiStatic.Address, UriKind.Absolute, out var uriResult) &&
                    (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    targetUri = new UriBuilder(uriResult);
                    if (MpiStatic.Port != 0) targetUri.Port = MpiStatic.Port;
                }
                else
                {
                    string addressWithScheme =
                        MpiStatic.Address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        MpiStatic.Address.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                            ? MpiStatic.Address
                            : "http://" + MpiStatic.Address;

                    targetUri = new UriBuilder(addressWithScheme);
                    if (MpiStatic.Port != 0) targetUri.Port = MpiStatic.Port;
                }

                var finalUri = targetUri.Uri;
                MpiStatic.Address = finalUri.AbsoluteUri;

                Timer.Reset();
                Timer.Start();

                var launchOptions = await _launchHelper.GetLauncher(_commandPath);

                using var browser = await Puppeteer.LaunchAsync(launchOptions);
                using var page = await browser.NewPageAsync();

                // Navigate and wait for network to go idle to reduce DOM churn
                var navOptions = new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                    Timeout = (int)Math.Max(0, MpiStatic.Timeout) // assumes milliseconds
                };
                await page.GoToAsync(finalUri.ToString(), navOptions);

                // Extra quiet window for late micro-mutations
                await page.WaitForNetworkIdleAsync(new WaitForNetworkIdleOptions { Timeout = (int)Math.Max(0, MpiStatic.Timeout) });
                await Task.Delay(500);

                // Deterministic text snapshot:
                //  - strip volatile nodes
                //  - use visible text (innerText)
                //  - normalize whitespace
                string snapshot = await page.EvaluateFunctionAsync<string>(
                    @"() => {
                try {
                    const sel = 'script,style,noscript,template,iframe,svg,canvas,meta,link[rel=""preload""],link[rel=""prefetch""]';
                    document.querySelectorAll(sel).forEach(n => n.remove());
                    const text = document.body?.innerText ?? '';
                    return text.replace(/\s+/g, ' ').trim();
                } catch (e) {
                    return '';
                }
            }");

                Timer.Stop();

                var hash = HashHelper.ComputeSha256Hash(snapshot);

                // First run: initialize and report OK
                if (string.IsNullOrWhiteSpace(MpiConnect.SiteHash))
                {
                    SetSiteHash(hash);
                    ProcessStatus("SiteHash initialized", (ushort)Timer.ElapsedMilliseconds, $"Hash: {hash}");
                    return;
                }

                if (hash.Equals(MpiConnect.SiteHash, StringComparison.OrdinalIgnoreCase))
                {
                    ProcessStatus("SiteHash OK", (ushort)Timer.ElapsedMilliseconds, $"Hash: {hash}");
                }
                else
                {
                    ProcessException(
                        $"SiteHash mismatch. Expected: {MpiConnect.SiteHash}, Got: {hash}",
                        "SiteHash Mismatch");
                }
            }
            catch (TaskCanceledException)
            {
                ProcessException($"Timed out after {MpiStatic.Timeout}", "Timeout");
            }
            catch (Exception e)
            {
                ProcessException(e.Message, "Exception");
            }
            finally
            {
                PostConnect();
            }
        }
    }
}
