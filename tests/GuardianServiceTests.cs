using WslPortProxyGuardian;

namespace WslPortProxyGuardian.Tests;

public sealed class GuardianServiceTests
{
    [Fact]
    public async Task CleanupOnlyRemovesOwnedMappings()
    {
        var resolver = new FakeResolver("172.28.98.2");
        var portProxyManager = new FakePortProxyManager();
        var firewallManager = new FakeFirewallRuleManager();
        var conflictDetector = new AllowAllConflictDetector();
        var logger = new FakeLogger();
        var service = new GuardianService(resolver, portProxyManager, firewallManager, conflictDetector, logger, new AllowPrivilegeChecker());

        var options = new CliOptions(
            ShowHelp: false,
            ShowRunHelp: false,
            Error: null,
            Mappings: [new PortMapping(4444, 4444)],
            Distro: "kali-linux",
            ListenAddress: "0.0.0.0",
            IntervalSeconds: 1,
            ManageFirewall: true,
            DryRun: true);

        await Assert.ThrowsAnyAsync<Exception>(() => service.RunAsync(options, new CancellationTokenSource(TimeSpan.FromMilliseconds(10)).Token));
        await service.CleanupAsync(options);

        Assert.Single(logger.Messages.Where(m => m.Contains("Removed managed mapping", StringComparison.Ordinal)));
    }

    [Fact]
    public void FirewallRuleNamesStayScoped()
    {
        Assert.Equal("WSLPortProxyGuardian [kali-linux] TCP 4444", RuleOwnership.FirewallRuleName("kali-linux", 4444));
    }

    [Fact]
    public async Task RefusesToManageOccupiedPorts()
    {
        var resolver = new FakeResolver("172.28.98.2");
        var portProxyManager = new FakePortProxyManager();
        var firewallManager = new FakeFirewallRuleManager();
        var conflictDetector = new BlockingConflictDetector(4444);
        var logger = new FakeLogger();
        var service = new GuardianService(resolver, portProxyManager, firewallManager, conflictDetector, logger, new AllowPrivilegeChecker());

        var options = new CliOptions(
            ShowHelp: false,
            ShowRunHelp: false,
            Error: null,
            Mappings: [new PortMapping(4444, 4444)],
            Distro: "kali-linux",
            ListenAddress: "0.0.0.0",
            IntervalSeconds: 1,
            ManageFirewall: true,
            DryRun: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(options, CancellationToken.None));
        Assert.DoesNotContain(portProxyManager.AppliedMappings, mapping => mapping.ListenPort == 4444);
    }

    [Fact]
    public async Task CleanupRemovesPortProxyWhenFirewallCreationFails()
    {
        var resolver = new FakeResolver("172.28.98.2");
        var portProxyManager = new FakePortProxyManager();
        var firewallManager = new FailingFirewallRuleManager();
        var conflictDetector = new AllowAllConflictDetector();
        var logger = new FakeLogger();
        var service = new GuardianService(resolver, portProxyManager, firewallManager, conflictDetector, logger, new AllowPrivilegeChecker());

        var options = new CliOptions(
            ShowHelp: false,
            ShowRunHelp: false,
            Error: null,
            Mappings: [new PortMapping(4444, 4444)],
            Distro: "kali-linux",
            ListenAddress: "0.0.0.0",
            IntervalSeconds: 1,
            ManageFirewall: true,
            DryRun: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(options, CancellationToken.None));
        await service.CleanupAsync(options);

        Assert.Contains(portProxyManager.RemovedMappings, mapping => mapping.ListenPort == 4444);
    }

    private sealed class FakeResolver(string address) : IWslAddressResolver
    {
        public Task<string> GetPrimaryAddressAsync(string distroName, CancellationToken cancellationToken) => Task.FromResult(address);
    }

    private sealed class FakePortProxyManager : IPortProxyManager
    {
        public List<PortMapping> AppliedMappings { get; } = [];
        public List<PortMapping> RemovedMappings { get; } = [];
        public Task ApplyAsync(string listenAddress, PortMapping mapping, string connectAddress, bool replaceExistingRule, bool dryRun, CancellationToken cancellationToken)
        {
            AppliedMappings.Add(mapping);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string listenAddress, PortMapping mapping, bool dryRun, CancellationToken cancellationToken)
        {
            RemovedMappings.Add(mapping);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFirewallRuleManager : IFirewallRuleManager
    {
        public Task EnsureAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingFirewallRuleManager : IFirewallRuleManager
    {
        public Task EnsureAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Firewall creation failed.");

        public Task RemoveAsync(string distroName, int listenPort, bool dryRun, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeLogger : ILogSink
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }

    private sealed class AllowPrivilegeChecker : IPrivilegeChecker
    {
        public void EnsureAdministrator()
        {
        }
    }

    private sealed class AllowAllConflictDetector : IPortConflictDetector
    {
        public Task<PortAvailability> EnsureAvailableAsync(string listenAddress, PortMapping mapping, PortProxyRule? ownedRule, CancellationToken cancellationToken) =>
            Task.FromResult(ownedRule is null ? PortAvailability.Available : PortAvailability.ReplaceOwned);
    }

    private sealed class BlockingConflictDetector(int blockedPort) : IPortConflictDetector
    {
        public Task<PortAvailability> EnsureAvailableAsync(string listenAddress, PortMapping mapping, PortProxyRule? ownedRule, CancellationToken cancellationToken)
        {
            if (mapping.ListenPort == blockedPort)
            {
                throw new InvalidOperationException($"Port {blockedPort} is already in use.");
            }

            return Task.FromResult(PortAvailability.Available);
        }
    }
}
