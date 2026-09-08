using System.Text;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace NetworkMonitor.Objects.Repository.Tests;

public sealed class GradLlmHmacProtocolTests
{
    [Fact]
    public void RequestSignatureMatchesPythonProtocolVector()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GradLlmMessageHmacKey"] = "test-gradllm-hmac-key-that-is-at-least-32-bytes"
        }).Build();
        var protocol = new GradLlmHmacProtocol(configuration);

        var envelope = protocol.CreateRequest(
            "oa.chat.create",
            "execute.api",
            "rk.test",
            Encoding.UTF8.GetBytes("{\"model\":\"timesfm\"}"));

        Assert.Equal("eyJtb2RlbCI6InRpbWVzZm0ifQ==", envelope.PayloadBase64);
        Assert.Equal("6eBOcKDpbFS0Ha1Zqi/V2KGP1qOuXz/kTH6D5U53FXk=", envelope.Signature);
    }
}
