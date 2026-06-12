namespace WslPortProxyGuardian;

public sealed class GuardianService(
    IWslAddressResolver addressResolver,
    IPortProxyManager portProxyManager,
    IFirewallRuleManager firewallRuleManager,
    IPortConflictDetector portConflictDetector,
    ILogSink logSink,
    IPrivilegeChecker? privilegeChecker = null,
    IConnectionMonitor? connectionMonitor = null,
    IForwardingDiagnostics? forwardingDiagnostics = null)
{
    private static readonly TimeSpan MinimumHeartbeatInterval = TimeSpan.FromMinutes(5);
    private readonly IPrivilegeChecker _privilegeChecker = privilegeChecker ?? new WindowsPrivilegeChecker();
    private readonly IConnectionMonitor _connectionMonitor = connectionMonitor ?? NullConnectionMonitor.Instance;
    private readonly IForwardingDiagnostics _forwardingDiagnostics = forwardingDiagnostics ?? NullForwardingDiagnostics.Instance;
    private readonly Dictionary<int, PortProxyRule> _ownedPortProxyRules = [];
    private readonly HashSet<int> _ownedFirewallPorts = [];
    private bool _cleanupCompleted;

    public async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        _privilegeChecker.EnsureAdministrator();

        logSink.Info($"Starting guardian for distro '{options.Distro}' with ports: {string.Join(", ", options.Mappings)}");
        if (options.DryRun)
        {
            logSink.Warn("Dry-run enabled. No system changes will be made.");
        }

        string? currentAddress = null;
        var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(options.IntervalSeconds * 20, (int)MinimumHeartbeatInterval.TotalSeconds));
        var nextHeartbeat = DateTimeOffset.UtcNow.Add(heartbeatInterval);
        var connectionDiagnosticInterval = TimeSpan.FromSeconds(Math.Max(options.IntervalSeconds * 5, 15));
        var nextConnectionDiagnostic = DateTimeOffset.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                var address = await addressResolver.GetPrimaryAddressAsync(options.Distro, cancellationToken);
                if (!string.Equals(address, currentAddress, StringComparison.Ordinal))
                {
                    logSink.Info(currentAddress is null
                        ? $"Resolved WSL IPv4 address: {address}"
                        : $"WSL IPv4 changed from {currentAddress} to {address}. Rebuilding managed mappings.");

                    await ReconcileAsync(options, address, cancellationToken);
                    currentAddress = address;
                    await InspectForwardingAsync(options, address, cancellationToken);
                }

                ObserveConnections(options, address);

                if (_connectionMonitor.ActiveConnectionCount > 0 && now >= nextConnectionDiagnostic)
                {
                    await InspectForwardingAsync(options, address, cancellationToken);
                    nextConnectionDiagnostic = now.Add(connectionDiagnosticInterval);
                }

                if (now >= nextHeartbeat)
                {
                    logSink.Info($"Heartbeat: WSL IPv4 remains {address}; {options.Mappings.Count} managed port(s) active; {_connectionMonitor.ActiveConnectionCount} observed forwarded connection(s).");
                    await InspectForwardingAsync(options, address, cancellationToken);
                    nextHeartbeat = DateTimeOffset.UtcNow.Add(heartbeatInterval);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logSink.Error($"Guardian loop failed: {ex.Message}");
                throw;
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
        foreach (var ownedRule in _ownedPortProxyRules.Values)
        {
            var mapping = ownedRule.Mapping;
            try
            {
                await portProxyManager.RemoveAsync(options.ListenAddress, mapping, options.DryRun, cts.Token);
                if (options.ManageFirewall && _ownedFirewallPorts.Contains(mapping.ListenPort))
                {
                    await firewallRuleManager.RemoveAsync(options.Distro, mapping.ListenPort, options.DryRun, cts.Token);
                }

                logSink.Info($"Removed managed mapping {mapping}.");
            }
            catch (Exception ex)
            {
                logSink.Error($"Failed to remove managed mapping {mapping}: {ex.Message}");
            }
        }
    }

    private async Task ReconcileAsync(CliOptions options, string connectAddress, CancellationToken cancellationToken)
    {
        foreach (var mapping in options.Mappings)
        {
            try
            {
                _ownedPortProxyRules.TryGetValue(mapping.ListenPort, out var ownedRule);
                var availability = await portConflictDetector.EnsureAvailableAsync(options.ListenAddress, mapping, ownedRule, cancellationToken);
                logSink.Info(availability.ReplaceExistingRule
                    ? $"Refreshing managed forwarding rule for TCP {options.ListenAddress}:{mapping.ListenPort}."
                    : $"Creating forwarding rule for TCP {options.ListenAddress}:{mapping.ListenPort}.");

                await portProxyManager.ApplyAsync(options.ListenAddress, mapping, connectAddress, availability.ReplaceExistingRule, options.DryRun, cancellationToken);
                _ownedPortProxyRules[mapping.ListenPort] = new PortProxyRule(options.ListenAddress, mapping.ListenPort, connectAddress, mapping.ConnectPort);

                if (options.ManageFirewall && !_ownedFirewallPorts.Contains(mapping.ListenPort))
                {
                    await firewallRuleManager.EnsureAsync(options.Distro, mapping.ListenPort, options.DryRun, cancellationToken);
                    if (!options.DryRun)
                    {
                        _ownedFirewallPorts.Add(mapping.ListenPort);
                    }
                }

                logSink.Info($"Forwarding TCP {options.ListenAddress}:{mapping.ListenPort} -> {connectAddress}:{mapping.ConnectPort}");
            }
            catch (Exception ex)
            {
                logSink.Error($"Failed to reconcile mapping {mapping}: {ex.Message}");
                throw;
            }
        }
    }

    private void ObserveConnections(CliOptions options, string connectAddress)
    {
        try
        {
            _connectionMonitor.Observe(options.ListenAddress, options.Mappings, connectAddress);
        }
        catch (Exception ex)
        {
            logSink.Error($"Failed to inspect forwarded TCP connections: {ex.Message}");
        }
    }

    private async Task InspectForwardingAsync(CliOptions options, string connectAddress, CancellationToken cancellationToken)
    {
        try
        {
            await _forwardingDiagnostics.InspectAsync(
                options,
                connectAddress,
                _connectionMonitor.ActiveConnectionCountsByListenPort,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logSink.Error($"Failed to run forwarding diagnostics: {ex.Message}");
        }
    }
}
