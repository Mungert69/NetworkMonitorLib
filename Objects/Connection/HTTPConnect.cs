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
using PuppeteerSharp;
namespace NetworkMonitor.Connection
{
    public class HTTPConnect : NetConnect
    {
        private HttpClient _client;
        private bool _isFullGet;
        private bool _isHtmlGet;
        private string _commandPath;
        public HTTPConnect(HttpClient client, bool isHtmlGet, bool isFullGet, string commandPath)
        {
            _client = client;
            _isFullGet = isFullGet;
            _isHtmlGet = isHtmlGet;
            _commandPath = commandPath;


        }


        public override async Task Connect()
        {
            PreConnect();
            HttpResponseMessage response; ;
            try
            {
                //bool portSpecifiedInUrl = false;
                UriBuilder targetUri;

                if (Uri.TryCreate(MpiStatic.Address, UriKind.Absolute, out var uriResult) &&
                    (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
                {
                    targetUri = new UriBuilder(uriResult);

                    // If a port was specified in the URL, set the flag to true.
                    if (uriResult.IsDefaultPort == false)
                    {
                        //portSpecifiedInUrl = true;
                        MpiStatic.Port = (ushort)uriResult.Port;
                    }
                }
                else
                {
                    string addressWithPort = MpiStatic.Address;

                    // Check if the scheme is missing and add "http://" as a default scheme.
                    if (!addressWithPort.StartsWith("http://") && !addressWithPort.StartsWith("https://"))
                    {
                        addressWithPort = "http://" + addressWithPort;
                    }

                    targetUri = new UriBuilder(addressWithPort);
                }

                // If MpiStatic.Port is not 0 and a port was not specified in the URL, replace the port in the UriBuilder.
                if (MpiStatic.Port != 0)
                {
                    targetUri.Port = MpiStatic.Port;
                }

                Uri finalUri = targetUri.Uri;
                MpiStatic.Address = finalUri.AbsoluteUri;


                Timer.Reset();
                if (!_isHtmlGet && _isFullGet)
                {
                    string statusStr = "";
                    // Launch a new browser instance
                    var lo = await LaunchHelper.GetLauncher(_commandPath);
                    using (var browser = await Puppeteer.LaunchAsync(lo))
                    {
                        
                        var page = await browser.NewPageAsync();
                        Timer.Start();
                        var pResponse = await page.GoToAsync(finalUri.ToString());
                        Timer.Stop();
                        statusStr = pResponse.Status.ToString();
                    }

                    ProcessStatus(statusStr, (ushort)Timer.ElapsedMilliseconds); 
                    //ProcessException("Warning httpfull Disabled use either http or httphtml", "FullHtml Disabled");
                }
                if (_isHtmlGet && !_isFullGet)
                {
                    // var sockerHttpHandler = new SocketsHttpHandler();
                    // sockerHttpHandler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                    // using (var httpClient = new HttpClient(sockerHttpHandler))
                    // {
                    Timer.Start();
                    response = await _client.GetAsync(finalUri, Cts.Token);
                    var stream = await response.Content.ReadAsStreamAsync();
                    using (var reader = new StreamReader(stream))
                    {
                        char[] buffer = new char[8192];
                        while (reader.Read(buffer, 0, buffer.Length) > 0)
                        {
                            // Do nothing with the buffer, we just want to read it all
                        }
                    }
                    Timer.Stop();
                    long? contentLength = response.Content.Headers.ContentLength;
                    string contentLengthString = "";
                    if (contentLength != null) contentLengthString = " : " + contentLength.ToString() + " bytes read";
                    ProcessStatus(response.StatusCode.ToString(), (ushort)Timer.ElapsedMilliseconds, contentLengthString);
                    //};

                }

                if (!_isFullGet && !_isHtmlGet)
                {
                    Timer.Start();

                    response = await _client.GetAsync(finalUri, Cts.Token);
                    Timer.Stop();
                    ProcessStatus(response.StatusCode.ToString(), (ushort)Timer.ElapsedMilliseconds);

                }
            }
            catch (TaskCanceledException)
            {
                ProcessException($"Timed out after {MpiStatic.Timeout}", "Timeout");
            }
            catch (HttpRequestException e)
            {
                string errorMessage = e.Message;

                // Check if there is an inner exception
                if (e.InnerException != null)
                {
                    // Append the message from the inner exception
                    errorMessage = e.InnerException.Message;

                    // Optionally, you can loop through all inner exceptions
                    // if there might be multiple levels of inner exceptions
                    Exception inner = e.InnerException;
                    while (inner.InnerException != null)
                    {
                        inner = inner.InnerException;
                        errorMessage += " -> " + inner.Message;
                    }
                }

                // Process the complete error message
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

        /*private async Task<string> GetDetailedStatus(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return "OK";
            }
            else if (response.StatusCode == HttpStatusCode.NotFound && !_isFullGet && !_isHtmlGet)
            {
                // If HEAD request returned 404, make a follow-up GET request
                var getResponse = await _client.GetAsync(response.RequestMessage.RequestUri, Cts.Token);
                if (getResponse.IsSuccessStatusCode)
                {
                    return "OK (HEAD not supported)";
                }
                else
                {
                    return "Not Found";
                }
            }
            else if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                return "Internal Server Error";
            }
            else if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                return "Service Unavailable";
            }
            else
            {
                return response.StatusCode.ToString();
            }
        }*/


    }
}