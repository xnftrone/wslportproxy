using System.Text.RegularExpressions;

namespace WslPortProxyGuardian;

public static class PortSpecParser
{
    private static readonly Regex PortPattern = new("^(?<listen>\\d{1,5})(:(?<connect>\\d{1,5}))?$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Expand(IEnumerable<string> portSpecs)
    {
        var values = new List<string>();
        foreach (var item in portSpecs)
        {
            foreach (var part in item.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    values.Add(part);
                }
            }
        }

        return values;
    }

    public static IReadOnlyList<PortMapping> ParseMany(IEnumerable<string> portSpecs)
    {
        return Expand(portSpecs).Select(ParseOne).ToArray();
    }

    public static PortMapping ParseOne(string portSpec)
    {
        var match = PortPattern.Match(portSpec);
        if (!match.Success)
        {
            throw new ArgumentException($"Invalid port specification: {portSpec}");
        }

        var listen = ParsePort(match.Groups["listen"].Value, portSpec);
        var connect = match.Groups["connect"].Success ? ParsePort(match.Groups["connect"].Value, portSpec) : listen;
        return new PortMapping(listen, connect);
    }

    private static int ParsePort(string value, string source)
    {
        var port = int.Parse(value);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentException($"Port out of range in specification: {source}");
        }

        return port;
    }
}
