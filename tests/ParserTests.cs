using WslPortProxyGuardian;

namespace WslPortProxyGuardian.Tests;

public sealed class ParserTests
{
    [Fact]
    public void ParsesRepeatableAndCommaSeparatedPorts()
    {
        var options = CliParser.Parse(["run", "-p", "4444,8000", "-p", "8443:443"]);

        Assert.False(options.ShowHelp);
        Assert.Collection(
            options.Mappings,
            mapping => Assert.Equal(new PortMapping(4444, 4444), mapping),
            mapping => Assert.Equal(new PortMapping(8000, 8000), mapping),
            mapping => Assert.Equal(new PortMapping(8443, 443), mapping));
    }

    [Fact]
    public void ReturnsHelpForMissingPorts()
    {
        var options = CliParser.Parse(["run"]);

        Assert.True(options.ShowHelp);
        Assert.Contains("required", options.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandsPortLists()
    {
        var expanded = PortSpecParser.Expand(["4444,8000", "8443:443", "9001"]);

        Assert.Equal(["4444", "8000", "8443:443", "9001"], expanded);
    }

    [Fact]
    public void RejectsInvalidPorts()
    {
        Assert.Throws<ArgumentException>(() => PortSpecParser.ParseOne("abc"));
        Assert.Throws<ArgumentException>(() => PortSpecParser.ParseOne("70000"));
    }
}
