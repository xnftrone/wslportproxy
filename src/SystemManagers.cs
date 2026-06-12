using System.Text.RegularExpressions;

namespace WslPortProxyGuardian;

public interface IPortProxyManager
{
    Task ApplyAsync(string listenAddress, PortMapping mapping, string connectAddress, bool replaceExistingRule, bool dryRun, CancellationToken cancellationToken);
    Task RemoveAsync(string listenAddress, PortMapping mapping, bool dryRun, CancellationToken cancellationToken);
}

public interface IPortConflictDetector
{
    Task<PortAvailability> EnsureAvailableAsync(string listenAddress, PortMapping mapping, PortProxyRule? ownedRule, CancellationToken cancellationToken);
}

public interface IFirewallRuleManager
{
    Task EnsureAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken);
    Task RemoveAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken);
}

public sealed class NetshPortProxyManager(IProcessRunner processRunner) : IPortProxyManager
{
    public async Task ApplyAsync(string listenAddress, PortMapping mapping, string connectAddress, bool replaceExistingRule, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return;
        }

        if (replaceExistingRule)
        {
            await DeleteAsync(listenAddress, mapping.ListenPort, cancellationToken);
        }

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
    private const string FirewallRuleExistsMarker = "WSLPORTPROXY_FIREWALL_RULE_EXISTS";
    private const string FirewallRuleMissingMarker = "WSLPORTPROXY_FIREWALL_RULE_MISSING";

    public async Task EnsureAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken)
    {
        if (dryRun)
        {
            return;
        }

        await EnsureRuleDoesNotAlreadyExistAsync(distroName, listenPort, cancellationToken);
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

    private async Task EnsureRuleDoesNotAlreadyExistAsync(string distroName, int listenPort, CancellationToken cancellationToken)
    {
        var name = RuleOwnership.FirewallRuleName(distroName, listenPort);
        var escapedName = EscapePowerShellSingleQuotedString(name);
        var script = "$ErrorActionPreference = 'Stop'; " +
            $"$ruleName = '{escapedName}'; " +
            "$rule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Select-Object -First 1; " +
            $"if ($null -eq $rule) {{ Write-Output '{FirewallRuleMissingMarker}'; exit 2 }}; " +
            $"Write-Output '{FirewallRuleExistsMarker}'; exit 0";
        var args = ProcessArgumentBuilder.Join(
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            script);
        var result = await processRunner.RunAsync("powershell.exe", args, cancellationToken);
        var output = $"{result.StandardOutput}{result.StandardError}";
        if (output.Contains(FirewallRuleExistsMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Firewall rule '{name}' already exists. Refusing to take over a pre-existing rule.");
        }

        if (output.Contains(FirewallRuleMissingMarker, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Unable to inspect existing firewall rule '{name}': {output}".Trim());
    }

    private static string EscapePowerShellSingleQuotedString(string value) => value.Replace("'", "''");

    private async Task RequireSuccessAsync(string arguments, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("netsh.exe", arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"netsh.exe {arguments} failed: {result.StandardError}{result.StandardOutput}".Trim());
        }
    }
}

public sealed class PortConflictDetector(IProcessRunner processRunner, ITcpStateProvider? tcpStateProvider = null) : IPortConflictDetector
{
    private readonly ITcpStateProvider _tcpStateProvider = tcpStateProvider ?? new SystemTcpStateProvider();

    private static readonly Regex PortProxyLinePattern = new(
        @"^(?<listenAddress>\S+)\s+(?<listenPort>\d+)\s+(?<connectAddress>\S+)\s+(?<connectPort>\d+)$",
        RegexOptions.Compiled);

    public async Task<PortAvailability> EnsureAvailableAsync(string listenAddress, PortMapping mapping, PortProxyRule? ownedRule, CancellationToken cancellationToken)
    {
        var matchingRules = await GetMatchingPortProxyRulesAsync(listenAddress, mapping.ListenPort, cancellationToken);
        if (matchingRules.Count > 0)
        {
            foreach (var existingRule in matchingRules)
            {
                if (ownedRule is not null && existingRule.MatchesOwnedRule(ownedRule))
                {
                    continue;
                }

                throw new InvalidOperationException($"Port {mapping.ListenPort} already has an existing portproxy rule on {existingRule.ListenAddress}. Refusing to take over a pre-existing mapping.");
            }

            return PortAvailability.ReplaceOwned;
        }

        EnsureNoActiveListener(listenAddress, mapping.ListenPort);
        return PortAvailability.Available;
    }

    private void EnsureNoActiveListener(string listenAddress, int listenPort)
    {
        var conflicts = _tcpStateProvider.GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == listenPort && ListenAddressMatcher.Matches(listenAddress, endpoint.Address));
        if (conflicts.Any())
        {
            throw new InvalidOperationException($"Port {listenPort} already has a host TCP listener. Refusing to override an active service.");
        }
    }

    private async Task<IReadOnlyList<PortProxyRule>> GetMatchingPortProxyRulesAsync(string listenAddress, int listenPort, CancellationToken cancellationToken)
    {
        var args = ProcessArgumentBuilder.Join("interface", "portproxy", "show", "v4tov4");
        var result = await processRunner.RunAsync("netsh.exe", args, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Unable to inspect existing portproxy rules: {result.StandardError}{result.StandardOutput}".Trim());
        }

        return ParsePortProxyRules(result.StandardOutput)
            .Where(rule => rule.ListenPort == listenPort && ListenAddressMatcher.Matches(listenAddress, rule.ListenAddress))
            .ToArray();
    }

    public static IReadOnlyList<PortProxyRule> ParsePortProxyRules(string text)
    {
        var rules = new List<PortProxyRule>();
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = PortProxyLinePattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            rules.Add(new PortProxyRule(
                match.Groups["listenAddress"].Value,
                int.Parse(match.Groups["listenPort"].Value),
                match.Groups["connectAddress"].Value,
                int.Parse(match.Groups["connectPort"].Value)));
        }

        return rules;
    }
}
