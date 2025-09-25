/*
A class named HTTPConnect which is used to perform an HTTP connection to a specific URL. The HTTPConnect class is a subclass of the NetConnect abstract class.
The main functionality of this class is implemented in the connect method which sends an HTTP HEAD request to the specified URL and processes the response. If a response is received within the specified timeout, the status of the response (e.g. "OK", "Not Found", etc.) is stored in the MonitorPingInfo object. If an error occurs, such as a timeout or a DNS resolution error, the error message is also stored in the MonitorPingInfo object.
Additionally, the HTTPConnect class has some utility functions to resolve the URL to an IP address, replace the host part of a URL, and process exceptions that might occur during the connection.
*/
using NetworkMonitor.Objects;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using System.Diagnostics;
using System.IO;
using PuppeteerSharp;

namespace NetworkMonitor.Connection
{
    public class HTTPConnect : NetConnect
    {
        private readonly IBrowserHost? _browserHost;        // preferred for httpfull to avoid spawning browsers
        private readonly HttpClient _client;
        private readonly bool _isFullGet;
        private readonly bool _isHtmlGet;
        private readonly string _commandPath;

        public HTTPConnect(
            HttpClient client,
            bool isHtmlGet,
            bool isFullGet,
            string commandPath,
              IBrowserHost? browserHost = null)
        {
            _client = client;
            _isFullGet = isFullGet;
            _isHtmlGet = isHtmlGet;
            _commandPath = commandPath;
               _browserHost = browserHost;
        }

        public override async Task Connect()
        {
            PreConnect();
            HttpResponseMessage response;
            try
            {
                UriBuilder targetUri;

                if (Uri.TryCreate(MpiStatic.Address, UriKind.Absolute, out var uriResult) &&
                    (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    targetUri = new UriBuilder(uriResult);

                    if (!uriResult.IsDefaultPort)
                    {
                        MpiStatic.Port = (ushort)uriResult.Port;
                    }
                }
                else
                {
                    string addressWithPort = MpiStatic.Address;

                    if (!addressWithPort.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                        !addressWithPort.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        addressWithPort = "http://" + addressWithPort;
                    }

                    targetUri = new UriBuilder(addressWithPort);
                }

                if (MpiStatic.Port != 0)
                {
                    targetUri.Port = MpiStatic.Port;
                }

                Uri finalUri = targetUri.Uri;
                MpiStatic.Address = finalUri.AbsoluteUri;

                Timer.Reset();

                // ——— Full page load (Puppeteer) ———
                if (_isFullGet && !_isHtmlGet)
                {
                    // Prefer shared BrowserHost; fall back to launcher only if absolutely necessary
                    if (_browserHost == null)
                    {
                        ProcessException("No browser available BrowserHost available.", "FullHtml Disabled");
                        return;
                    }

                    string statusStr = "";


                    statusStr = await _browserHost.RunWithPage(async page =>
                    {
                        var navOptions = new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                            Timeout = (int)Math.Max(0, MpiStatic.Timeout)
                        };

                        Timer.Start();
                        var pResponse = await page.GoToAsync(finalUri.ToString(), navOptions);
                        Timer.Stop();
                        return pResponse.Status.ToString();
                    }, Cts.Token);



                    ProcessStatus(statusStr, (ushort)Timer.ElapsedMilliseconds);
                    return;
                }

                // ——— HTML GET (stream through) ———
                if (_isHtmlGet && !_isFullGet)
                {
                    Timer.Start();
                    response = await _client.GetAsync(finalUri, Cts.Token);
                    var stream = await response.Content.ReadAsStreamAsync(Cts.Token);
                    using (var reader = new StreamReader(stream))
                    {
                        char[] buffer = new char[8192];
                        while (await reader.ReadAsync(buffer, 0, buffer.Length) > 0) { /* just drain */ }
                    }
                    Timer.Stop();

                    long? contentLength = response.Content.Headers.ContentLength;
                    string contentLengthString = contentLength != null ? $" : {contentLength} bytes read" : "";
                    ProcessStatus(response.StatusCode.ToString(), (ushort)Timer.ElapsedMilliseconds, contentLengthString);
                    return;
                }

                // ——— Lightweight GET (status only) ———
                if (!_isFullGet && !_isHtmlGet)
                {
                    Timer.Start();
                    response = await _client.GetAsync(finalUri, Cts.Token);
                    Timer.Stop();
                    ProcessStatus(response.StatusCode.ToString(), (ushort)Timer.ElapsedMilliseconds);
                    return;
                }
            }
            catch (TaskCanceledException)
            {
                ProcessException($"Timed out after {MpiStatic.Timeout}", "Timeout");
            }
            catch (HttpRequestException e)
            {
                string errorMessage = e.Message;

                if (e.InnerException != null)
                {
                    errorMessage = e.InnerException.Message;
                    Exception inner = e.InnerException;
                    while (inner.InnerException != null)
                    {
                        inner = inner.InnerException;
                        errorMessage += " -> " + inner.Message;
                    }
                }

                ProcessException(errorMessage, "HttpRequestException");
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
