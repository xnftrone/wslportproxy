namespace WslPortProxyGuardian;

public static class RuleOwnership
{
    public static string FirewallRuleName(string distroName, int listenPort) =>
        $"WSLPortProxyGuardian [{distroName}] TCP {listenPort}";
}
