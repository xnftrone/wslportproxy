namespace WslPortProxyGuardian;

public sealed record CliOptions(
    bool ShowHelp,
    bool ShowRunHelp,
    string? Error,
    IReadOnlyList<PortMapping> Mappings,
    string Distro,
    string ListenAddress,
    int IntervalSeconds,
    bool ManageFirewall,
    bool DryRun)
{
    public static CliOptions Help(bool runHelp = false, string? error = null) =>
        new(true, runHelp, error, Array.Empty<PortMapping>(), "kali-linux", "0.0.0.0", 3, true, false);
}
