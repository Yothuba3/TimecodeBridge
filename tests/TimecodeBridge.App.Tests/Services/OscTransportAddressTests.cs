using System.Net.Sockets;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;

namespace TimecodeBridge.App.Tests.Services;

public class OscTransportAddressTests
{
    [Theory]
    [InlineData("127.0.0.1", AddressFamily.InterNetwork)]
    [InlineData("192.168.1.255", AddressFamily.InterNetwork)]
    [InlineData("255.255.255.255", AddressFamily.InterNetwork)]
    [InlineData(" 10.0.0.1 ", AddressFamily.InterNetwork)]
    [InlineData("::1", AddressFamily.InterNetworkV6)]
    [InlineData("2001:db8::1", AddressFamily.InterNetworkV6)]
    public void TryParseIpAddress_AcceptsIpLiterals(string text, AddressFamily expectedFamily)
    {
        Assert.True(OscHost.TryParseIpAddress(text, out var address));
        Assert.Equal(expectedFamily, address.AddressFamily);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("qlab.local")]
    [InlineData("localhost")]
    [InlineData("192.168.1.10:53000")]
    [InlineData("192.168.1")]   // IPAddress.TryParse だけだと 192.168.0.1 として通る
    [InlineData("1")]           // 同上、0.0.0.1 として通る
    [InlineData("192.168.001.010")]
    public void TryParseIpAddress_RejectsHostNamesAndShorthand(string text)
    {
        Assert.False(OscHost.TryParseIpAddress(text, out _));
    }

    [Fact]
    public void Send_HostName_ThrowsWithoutResolving()
    {
        var transport = new OscTransport();

        var ex = Assert.Throws<ArgumentException>(() => transport.Send("qlab.local", 53000, "/cue", []));

        Assert.Contains("qlab.local", ex.Message);
    }
}
