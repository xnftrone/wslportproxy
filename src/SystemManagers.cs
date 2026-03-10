using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace WslPortProxyGuardian;

public interface IPortProxyManager
{
    Task ApplyAsync(string listenAddress, PortMapping mapping, string connectAddress, bool dryRun, CancellationToken cancellationToken);
    Task RemoveAsync(string listenAddress, PortMapping mapping, bool dryRun, CancellationToken cancellationToken);
}

public interface IPortConflictDetector
{
    Task EnsureAvailableAsync(string listenAddress, PortMapping mapping, CancellationToken cancellationToken);
}

public interface IFirewallRuleManager
{
    Task EnsureAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken);
    Task RemoveAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken);
}

public sealed class NetshPortProxyManager(IProcessRunner processRunner) : IPortProxyManager
{
    public async Task ApplyAsync(string listenAddress, PortMapping mapping, string connectAddress, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return;
        }

        await DeleteAsync(listenAddress, mapping.ListenPort, cancellationToken);
        var addArgs = ProcessArgumentBuilder.Join(
            "interface", "portproxy", "add", "v4tov4",
            $"listenaddress={listenAddress}",
            $"listenport={mapping.ListenPort}",
            $"connectaddress={connectAddress}",
            $"connectport={mapping.ConnectPort}");
        await RequireSuccessAsync("netsh.exe", addArgs, cancellationToken);
    }

    public async Task RemoveAsync(string listenAddress, PortMapping mapping, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return;
        }

        await DeleteAsync(listenAddress, mapping.ListenPort, cancellationToken);
    }

    private async Task DeleteAsync(string listenAddress, int listenPort, CancellationToken cancellationToken)
    {
        var deleteArgs = ProcessArgumentBuilder.Join(
            "interface", "portproxy", "delete", "v4tov4",
            $"listenaddress={listenAddress}",
            $"listenport={listenPort}");
        await processRunner.RunAsync("netsh.exe", deleteArgs, cancellationToken);
    }

    private async Task RequireSuccessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(fileName, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments} failed: {result.StandardError}{result.StandardOutput}".Trim());
        }
    }
}

public sealed class NetshFirewallRuleManager(IProcessRunner processRunner) : IFirewallRuleManager
{
    public async Task EnsureAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return;
        }

        await RemoveAsync(distroName, listenPort, dryRun: false, cancellationToken);
        var args = ProcessArgumentBuilder.Join(
            "advfirewall", "firewall", "add", "rule",
            $"name={RuleOwnership.FirewallRuleName(distroName, listenPort)}",
            "dir=in",
            "action=allow",
            "protocol=TCP",
            $"localport={listenPort}");
        await RequireSuccessAsync(args, cancellationToken);
    }

    public async Task RemoveAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return;
        }

        var args = ProcessArgumentBuilder.Join(
            "advfirewall", "firewall", "delete", "rule",
            $"name={RuleOwnership.FirewallRuleName(distroName, listenPort)}");
        await processRunner.RunAsync("netsh.exe", args, cancellationToken);
    }

    private async Task RequireSuccessAsync(string arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("netsh.exe", arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh.exe {arguments} failed: {result.StandardError}{result.StandardOutput}".Trim());
        }
    }
}

public sealed class PortConflictDetector(IProcessRunner processRunner) : IPortConflictDetector
{
    private static readonly Regex PortProxyLinePattern = new(
        @"^(?<listenAddress>\S+)\s+(?<listenPort>\d+)\s+(?<connectAddress>\S+)\s+(?<connectPort>\d+)$",
        RegexOptions.Compiled);

    public async Task EnsureAvailableAsync(string listenAddress, PortMapping mapping, CancellationToken cancellationToken)
    {
        EnsureNoActiveListener(listenAddress, mapping.ListenPort);
        await EnsureNoForeignPortProxyAsync(listenAddress, mapping.ListenPort, cancellationToken);
    }

    private static void EnsureNoActiveListener(string listenAddress, int listenPort)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        var conflicts = listeners.Where(endpoint => endpoint.Port == listenPort && ListenAddressMatches(listenAddress, endpoint.Address));
        if (conflicts.Any())
        {
            throw new InvalidOperationException($"Port {listenPort} already has a host TCP listener. Refusing to override an active service.");
        }
    }

    private async Task EnsureNoForeignPortProxyAsync(string listenAddress, int listenPort, CancellationToken cancellationToken)
    {
        var args = ProcessArgumentBuilder.Join("interface", "portproxy", "show", "v4tov4");
        var result = await processRunner.RunAsync("netsh.exe", args, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Unable to inspect existing portproxy rules: {result.StandardError}{result.StandardOutput}".Trim());
        }

        foreach (var line in result.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = PortProxyLinePattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var existingListenAddress = match.Groups["listenAddress"].Value;
            var existingListenPort = int.Parse(match.Groups["listenPort"].Value);
            if (existingListenPort != listenPort)
            {
                continue;
            }

            if (ListenAddressMatches(listenAddress, existingListenAddress))
            {
                throw new InvalidOperationException($"Port {listenPort} already has an existing portproxy rule on {existingListenAddress}. Refusing to take over a pre-existing mapping.");
            }
        }
    }

    private static bool ListenAddressMatches(string requested, string existing)
    {
        if (string.Equals(requested, "0.0.0.0", StringComparison.Ordinal) || string.Equals(existing, "0.0.0.0", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(requested, "*", StringComparison.Ordinal) || string.Equals(existing, "*", StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(requested, existing, StringComparison.OrdinalIgnoreCase)
            || IPAddressMatches(requested, existing);
    }

    private static bool ListenAddressMatches(string requested, System.Net.IPAddress existing)
    {
        if (string.Equals(requested, "0.0.0.0", StringComparison.Ordinal))
        {
            return existing.AddressFamily == AddressFamily.InterNetwork;
        }

        return System.Net.IPAddress.TryParse(requested, out var requestedIp)
            && EqualityComparer<System.Net.IPAddress>.Default.Equals(requestedIp, existing);
    }

    private static bool IPAddressMatches(string left, string right)
    {
        return System.Net.IPAddress.TryParse(left, out var leftIp)
            && System.Net.IPAddress.TryParse(right, out var rightIp)
            && EqualityComparer<System.Net.IPAddress>.Default.Equals(leftIp, rightIp);
    }
}
