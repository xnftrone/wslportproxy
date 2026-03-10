using System.Security.Principal;

namespace WslPortProxyGuardian;

public sealed class GuardianService(
    IWslAddressResolver addressResolver,
    IPortProxyManager portProxyManager,
    IFirewallRuleManager firewallRuleManager,
    IPortConflictDetector portConflictDetector,
    ILogSink logSink)
{
    private readonly HashSet<PortMapping> _ownedMappings = [];
    private bool _cleanupCompleted;

    public async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        EnsureAdministrator();

        logSink.Info($"Starting guardian for distro '{options.Distro}' with ports: {string.Join(", ", options.Mappings)}");
        if (options.DryRun)
        {
            logSink.Warn("Dry-run enabled. No system changes will be made.");
        }

        string? currentAddress = null;
        var nextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(Math.Max(options.IntervalSeconds * 5, 30));

        while (!cancellationToken.IsCancellationRequested)
        {
            var address = await addressResolver.GetPrimaryAddressAsync(options.Distro, cancellationToken);
            if (!string.Equals(address, currentAddress, StringComparison.Ordinal))
            {
                logSink.Info(currentAddress is null
                    ? $"Resolved WSL IPv4 address: {address}"
                    : $"WSL IPv4 changed from {currentAddress} to {address}. Rebuilding managed mappings.");

                await ReconcileAsync(options, address, cancellationToken);
                currentAddress = address;
            }
            else if (DateTimeOffset.UtcNow >= nextHeartbeat)
            {
                logSink.Info($"Heartbeat: WSL IPv4 remains {address}; {options.Mappings.Count} managed port(s) active.");
                nextHeartbeat = DateTimeOffset.UtcNow.AddSeconds(Math.Max(options.IntervalSeconds * 5, 30));
            }

            await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), cancellationToken);
        }

        return 0;
    }

    public async Task CleanupAsync(CliOptions options)
    {
        if (_cleanupCompleted)
        {
            return;
        }

        _cleanupCompleted = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var mapping in _ownedMappings)
        {
            try
            {
                await portProxyManager.RemoveAsync(options.ListenAddress, mapping, options.DryRun, cts.Token);
                if (options.ManageFirewall)
                {
                    await firewallRuleManager.RemoveAsync(options.Distro, mapping.ListenPort, options.DryRun, cts.Token);
                }

                logSink.Info($"Removed managed mapping {mapping}.");
            }
            catch (Exception ex)
            {
                logSink.Warn($"Failed to remove managed mapping {mapping}: {ex.Message}");
            }
        }
    }

    private async Task ReconcileAsync(CliOptions options, string connectAddress, CancellationToken cancellationToken)
    {
        foreach (var mapping in options.Mappings)
        {
            await portConflictDetector.EnsureAvailableAsync(options.ListenAddress, mapping, cancellationToken);
            await portProxyManager.ApplyAsync(options.ListenAddress, mapping, connectAddress, options.DryRun, cancellationToken);
            if (options.ManageFirewall)
            {
                await firewallRuleManager.EnsureAsync(options.Distro, mapping.ListenPort, options.DryRun, cancellationToken);
            }

            _ownedMappings.Add(mapping);
            logSink.Info($"Managed TCP {options.ListenAddress}:{mapping.ListenPort} -> {connectAddress}:{mapping.ConnectPort}");
        }
    }

    private static void EnsureAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException("Run this tool from an elevated terminal.");
        }
    }
}
