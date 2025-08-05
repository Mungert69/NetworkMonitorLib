using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using Moq;
using Moq.Protected;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects;
using Xunit;
using PuppeteerSharp;

namespace NetworkMonitorLib.Tests.Objects.Connection
{
    public class HTTPConnectTests
    {
        private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, string content = "test", Exception? exception = null)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            if (exception == null)
            {
                handlerMock
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>()
                    )
                    .ReturnsAsync(new HttpResponseMessage
                    {
                        StatusCode = statusCode,
                        Content = new StringContent(content)
                    })
                    .Verifiable();
            }
            else
            {
                handlerMock
                    .Protected()
                    .Setup<Task<HttpResponseMessage>>(
                        "SendAsync",
                        ItExpr.IsAny<HttpRequestMessage>(),
                        ItExpr.IsAny<CancellationToken>()
                    )
                    .ThrowsAsync(exception)
                    .Verifiable();
            }

            return new HttpClient(handlerMock.Object);
        }

        [Fact]
        public async Task Connect_BasicGet_SetsStatus()
        {
            var client = CreateMockHttpClient(HttpStatusCode.OK);
            var httpConnect = new HTTPConnect(client, isHtmlGet: false, isFullGet: false, commandPath: "");
            httpConnect.MpiStatic = new MPIStatic { Address = "http://localhost", Port = 0, Timeout = 1000, EndPointType = "http" };

            await httpConnect.Connect();

            Assert.True(httpConnect.MpiConnect.IsUp);
            Assert.Equal("OK", httpConnect.MpiConnect.PingInfo.Status);
        }

        [Fact]
        public async Task Connect_HtmlGet_SetsStatusAndContentLength()
        {
            var content = new string('a', 100);
            var client = CreateMockHttpClient(HttpStatusCode.OK, content);
            var httpConnect = new HTTPConnect(client, isHtmlGet: true, isFullGet: false, commandPath: "");
            httpConnect.MpiStatic = new MPIStatic { Address = "http://localhost", Port = 0, Timeout = 1000, EndPointType = "http" };

            await httpConnect.Connect();

            Assert.True(httpConnect.MpiConnect.IsUp);
            Assert.Contains("OK", httpConnect.MpiConnect.PingInfo.Status);
            Assert.Contains("bytes read", httpConnect.MpiConnect.Message);
        }

        [Fact]
        public async Task Connect_FullGet_PuppeteerMissing_SetsException()
        {
            var client = CreateMockHttpClient(HttpStatusCode.OK);
            var httpConnect = new HTTPConnect(client, isHtmlGet: false, isFullGet: true, commandPath: "", launchHelper: null);
            httpConnect.MpiStatic = new MPIStatic { Address = "http://localhost", Port = 0, Timeout = 1000, EndPointType = "http" };

            await httpConnect.Connect();

            Assert.False(httpConnect.MpiConnect.IsUp);
            Assert.Contains("PuppeteerSharp browser is missing", httpConnect.MpiConnect.Message);
        }

        [Fact]
        public async Task Connect_Timeout_SetsTimeoutException()
        {
            var client = CreateMockHttpClient(HttpStatusCode.OK, exception: new TaskCanceledException());
            var httpConnect = new HTTPConnect(client, isHtmlGet: false, isFullGet: false, commandPath: "");
            httpConnect.MpiStatic = new MPIStatic { Address = "http://localhost", Port = 0, Timeout = 1000, EndPointType = "http" };

            await httpConnect.Connect();

            Assert.False(httpConnect.MpiConnect.IsUp);
            Assert.Contains("Timed out", httpConnect.MpiConnect.Message);
        }

        [Fact]
        public async Task Connect_HttpRequestException_SetsHttpRequestException()
        {
            var client = CreateMockHttpClient(HttpStatusCode.OK, exception: new HttpRequestException("DNS error"));
            var httpConnect = new HTTPConnect(client, isHtmlGet: false, isFullGet: false, commandPath: "");
            httpConnect.MpiStatic = new MPIStatic { Address = "http://localhost", Port = 0, Timeout = 1000, EndPointType = "http" };

            await httpConnect.Connect();

            Assert.False(httpConnect.MpiConnect.IsUp);
            Assert.Contains("DNS error", httpConnect.MpiConnect.Message);
        }

        [Fact]
        public async Task Connect_GenericException_SetsException()
        {
            var client = CreateMockHttpClient(HttpStatusCode.OK, exception: new Exception("Some error"));
            var httpConnect = new HTTPConnect(client, isHtmlGet: false, isFullGet: false, commandPath: "");
            httpConnect.MpiStatic = new MPIStatic { Address = "http://localhost", Port = 0, Timeout = 1000, EndPointType = "http" };

            await httpConnect.Connect();

            Assert.False(httpConnect.MpiConnect.IsUp);
            Assert.Contains("Some error", httpConnect.MpiConnect.Message);
        }

        // [Fact]
        // public async Task Connect_FullGet_Puppeteer_SetsStatus()
        // {
        //     // This test is not possible without a wrapper for Puppeteer.LaunchAsync (static method).
        // }
    }
}
