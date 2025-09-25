using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using System;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace NetworkMonitor.Connection
{
    public class SiteHashConnect : NetConnect
    {
        private readonly IBrowserHost? _browserHost;
        private readonly string _commandPath;

        public SiteHashConnect(string commandPath, IBrowserHost? browserHost = null)
        {
            _commandPath = commandPath;
            _browserHost = browserHost;
        }

        public override async Task Connect()
        {
            PreConnect();
            try
            {
                if (_browserHost == null)
                {
                    ProcessException("BrowserHost is missing, check initialization", "Browser Missing");
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
                    var addressWithScheme =
                        (MpiStatic.Address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         MpiStatic.Address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        ? MpiStatic.Address
                        : "http://" + MpiStatic.Address;

                    targetUri = new UriBuilder(addressWithScheme);
                    if (MpiStatic.Port != 0) targetUri.Port = MpiStatic.Port;
                }

                var finalUri = targetUri.Uri;
                MpiStatic.Address = finalUri.AbsoluteUri;

                // Micro budget for individual waits
                var micro = (int)Math.Clamp(MpiStatic.Timeout == 0 ? 10_000 : MpiStatic.Timeout, 1_000, 60_000);

                Timer.Reset();
                Timer.Start();

                var snapshot = await _browserHost.RunWithPage(async page =>
                {
                    // Navigate (avoid Networkidle*, use DOMContentLoaded instead)
                    await WebAutomationHelper.GoToWithCacheBusterAsync(
                        page,
                        finalUri.ToString(),
                        nav: new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                            Timeout = micro
                        });

                    // Try to settle a bit, but don't throw if it never goes idle
                    await WebAutomationHelper.WaitForNetworkIdleSafeAsync(
                        page,
                        idleMs: 800,
                        timeoutMs: Math.Min(8_000, micro));

                    await Task.Delay(500); // allow tiny micro-mutations

                    // Deterministic text snapshot
                    var text = await page.EvaluateFunctionAsync<string>(
                        @"() => {
                            try {
                                const sel = 'script,style,noscript,template,iframe,svg,canvas,meta,link[rel=""preload""],link[rel=""prefetch""]';
                                document.querySelectorAll(sel).forEach(n => n.remove());
                                const t = document.body?.innerText ?? '';
                                return t.replace(/\s+/g, ' ').trim();
                            } catch(e) { return ''; }
                        }");

                    return text ?? string.Empty;
                });

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
