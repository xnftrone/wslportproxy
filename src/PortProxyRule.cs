namespace WslPortProxyGuardian;

public sealed record PortProxyRule(string ListenAddress, int ListenPort, string ConnectAddress, int ConnectPort)
{
    public PortMapping Mapping => new(ListenPort, ConnectPort);

    public bool MatchesOwnedRule(PortProxyRule ownedRule)
    {
        return ListenPort == ownedRule.ListenPort
            && ConnectPort == ownedRule.ConnectPort
            && SameListenAddress(ownedRule.ListenAddress, ListenAddress)
            && string.Equals(ConnectAddress, ownedRule.ConnectAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameListenAddress(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Net.IPAddress.TryParse(left, out var leftIp)
            && System.Net.IPAddress.TryParse(right, out var rightIp)
            && EqualityComparer<System.Net.IPAddress>.Default.Equals(leftIp, rightIp);
    }
}

public sealed record PortAvailability(bool ReplaceExistingRule)
{
    public static PortAvailability Available { get; } = new(false);
    public static PortAvailability ReplaceOwned { get; } = new(true);
}
