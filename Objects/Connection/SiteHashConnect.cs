// SiteHashConnect.cs
using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using PuppeteerSharp;

namespace NetworkMonitor.Connection
{
    public class SiteHashConnect : NetConnect
    {
        private ILaunchHelper? _launchHelper;
        private string _commandPath;

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

                UriBuilder targetUri;
                if (Uri.TryCreate(MpiStatic.Address, UriKind.Absolute, out var uriResult) &&
                    (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    targetUri = new UriBuilder(uriResult);
                    if (MpiStatic.Port != 0)
                    {
                        targetUri.Port = MpiStatic.Port;
                    }
                }
                else
                {
                    string addressWithPort = MpiStatic.Address;
                    if (!addressWithPort.StartsWith("http://") && !addressWithPort.StartsWith("https://"))
                    {
                        addressWithPort = "http://" + addressWithPort;
                    }
                    targetUri = new UriBuilder(addressWithPort);
                    if (MpiStatic.Port != 0)
                    {
                        targetUri.Port = MpiStatic.Port;
                    }
                }

                Uri finalUri = targetUri.Uri;
                MpiStatic.Address = finalUri.AbsoluteUri;

                Timer.Reset();
                Timer.Start();

                var lo = await _launchHelper.GetLauncher(_commandPath);
                using (var browser = await Puppeteer.LaunchAsync(lo))
                {
                    var page = await browser.NewPageAsync();
                    var pResponse = await page.GoToAsync(finalUri.ToString());
                    string html = await page.GetContentAsync();
                    Timer.Stop();

                    // Compute hash
                    string hash = HashHelper.ComputeSha256Hash(html);

                    // Compare to expected hash
                    if (string.IsNullOrEmpty(MpiStatic.SiteHash))
                    {
                       SetSiteHash(hash);
                    }

                    if (hash.Equals(MpiStatic.SiteHash, StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStatus("SiteHash OK", (ushort)Timer.ElapsedMilliseconds, $"Hash: {hash}");
                    }
                    else
                    {
                        ProcessException($"SiteHash mismatch. Expected: {MpiStatic.SiteHash}, Got: {hash}", "SiteHash Mismatch");
                    }
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
