using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NetworkMonitor.Objects;
using NetworkMonitor.Utils.Helpers;
using Xunit;

namespace NetworkMonitorLib.Tests.Helpers;

public class APIHelperTests
{
    private static HttpListener StartListener(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var listener = new HttpListener();
        var port = TestUtilities.GetFreeTcpPort();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        _ = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var buffer = Encoding.UTF8.GetBytes(responseBody);
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.OutputStream.Close();
            listener.Stop();
            listener.Close();
        });

        return listener;
    }

    [Fact]
    public async Task GetDataFromResultObjJson_ReturnsParsedPayload()
    {
        var payload = "{\"message\":\"ok\",\"success\":true,\"data\":\"{\\\"ExternalUrl\\\":\\\"https://site\\\"}\"}";
        var listener = StartListener(payload);
        var uri = listener.Prefixes.First();

        var result = await APIHelper.GetDataFromResultObjJson<SystemUrl>(uri);

        Assert.True(result.Success);
        Assert.Equal("ok", result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal("https://site", result.Data!.ExternalUrl);
    }

    [Fact]
    public async Task GetJson_HandlesHttpErrors()
    {
        var listener = StartListener("Internal Server Error", HttpStatusCode.InternalServerError);
        var uri = listener.Prefixes.First();

        var result = await APIHelper.GetJson<SystemUrl>(uri);

        Assert.False(result.Success);
        Assert.Contains("Error in APIHelper.GetJson", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetJsonResult_ReadsIntegerPayload()
    {
        var payload = "{\"message\":\"ignored\",\"success\":true,\"data\":5}";
        var listener = StartListener(payload);
        var uri = listener.Prefixes.First();

        var result = await APIHelper.GetJsonResult(uri);

        Assert.True(result.Success);
        Assert.Equal("Got load 5", result.Message);
        Assert.Equal(5, result.Data);
    }
}
