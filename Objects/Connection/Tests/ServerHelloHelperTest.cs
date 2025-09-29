using System;
using System.Text;
using Xunit;

public class ServerHelloHelperTests
{
    [Fact]
    public void FindServerHello_ReturnsDefaultKemExtension_WhenNoServerHello()
    {
        var helper = new ServerHelloHelper();
        var result = helper.FindServerHello("Certificate\nEncryptedExtensions\n");
        Assert.False(result.IsQuantumSafe);
        Assert.Equal(0, result.GroupID);
    }

    [Fact]
    public void FindServerHello_ReturnsDefaultKemExtension_WhenInvalidHex()
    {
        var helper = new ServerHelloHelper();
        var result = helper.FindServerHello("ServerHello\nnothexdata\n");
        Assert.False(result.IsQuantumSafe);
        Assert.Equal(0, result.GroupID);
    }

    [Fact]
    public void FindServerHello_ParsesMinimalValidServerHello()
    {
        var helper = new ServerHelloHelper();
        // TLS 1.2 ServerHello: handshake type (0x02), length (0x00 00 28 = 40 bytes)
        // version(2), random(32), sessionidlen(1), ciphersuite(2), comp(1), extlen(2, zero)
        // sessionidlen(1) = 0, so no sessionid
        // 2+32+1+0+2+1+2 = 40
        string body = "0303" + new string('0', 64) + "00" + "1301" + "00" + "0000";
        string hex = "02000028" + body;
        var input = $"ServerHello\n{hex}\n";
        var result = helper.FindServerHello(input);
        Assert.False(result.IsQuantumSafe);
        Assert.Equal(0, result.GroupID);
    }

    [Fact]
    public void FindServerHello_ParsesKeyShareExtension_QuantumSafe()
    {
        var helper = new ServerHelloHelper();
        // TLS 1.3 ServerHello with key_share extension (0x0033)
        // Use built-in PQ hybrid group 0x11EB so helper recognises it as quantum safe.
        string body = "0303" + new string('0', 64) + "00" + "1301" + "00" + "0006" + "0033000211EB";
        string hex = "0200002E" + body;
        var input = $"ServerHello\n{hex}\n";
        var result = helper.FindServerHello(input);
        Assert.True(result.IsQuantumSafe);
        Assert.Equal(0x11EB, result.GroupID);
        Assert.Equal("0x11EB", result.GroupHexStringID);
    }

    [Fact]
    public void DecodeKeyShareExtension_ReturnsNotQuantumSafe_ForNonKeyShare()
    {
        var helper = new ServerHelloHelper();
        var ext = new System.Collections.Generic.KeyValuePair<int, byte[]>(0x0000, new byte[] { 0x01, 0x02 });
        var kem = helper.DecodeKeyShareExtension(ext);
        Assert.False(kem.IsQuantumSafe);
    }
}
