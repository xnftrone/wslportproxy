using WslPortProxyGuardian;

namespace WslPortProxyGuardian.Tests;

public sealed class ResolverAndHelpTests
{
    [Fact]
    public void ParsesFirstIpv4Address()
    {
        var address = WslAddressResolver.ParseAddress("172.28.98.229 fe80::215:5dff:fe00:1234");

        Assert.Equal("172.28.98.229", address);
    }

    [Fact]
    public void ThrowsWhenNoIpv4AddressExists()
    {
        Assert.Throws<InvalidOperationException>(() => WslAddressResolver.ParseAddress("fe80::215:5dff:fe00:1234"));
    }

    [Fact]
    public void HelpTextIncludesRunSyntax()
    {
        var help = HelpText.Render(runHelp: false);

        Assert.Contains("wslportproxy run", help, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--help", help, StringComparison.OrdinalIgnoreCase);
    }
}
