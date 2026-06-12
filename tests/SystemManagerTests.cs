using System.Net.NetworkInformation;
using WslPortProxyGuardian;

namespace WslPortProxyGuardian.Tests;

public sealed class SystemManagerTests
{
    [Fact]
    public async Task AllowsRefreshingOwnedPortProxyRule()
    {
        var detector = new PortConflictDetector(
            new FakeProcessRunner("""
                                  Listen on ipv4:             Connect to ipv4:
                                  Address         Port        Address         Port
                                  --------------- ----------  --------------- ----------
                                  0.0.0.0         4444        172.28.98.2     4444
                                  """),
            new FakeTcpStateProvider());

        var availability = await detector.EnsureAvailableAsync(
            "0.0.0.0",
            new PortMapping(4444, 4444),
            new PortProxyRule("0.0.0.0", 4444, "172.28.98.2", 4444),
            CancellationToken.None);

        Assert.True(availability.ReplaceExistingRule);
    }

    [Fact]
    public async Task RefusesForeignPortProxyRule()
    {
        var detector = new PortConflictDetector(
            new FakeProcessRunner("""
                                  Listen on ipv4:             Connect to ipv4:
                                  Address         Port        Address         Port
                                  --------------- ----------  --------------- ----------
                                  0.0.0.0         4444        172.28.98.99    4444
                                  """),
            new FakeTcpStateProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(() => detector.EnsureAvailableAsync(
            "0.0.0.0",
            new PortMapping(4444, 4444),
            new PortProxyRule("0.0.0.0", 4444, "172.28.98.2", 4444),
            CancellationToken.None));
    }

    [Fact]
    public async Task RefusesWildcardHostListenerForSpecificListenAddress()
    {
        var detector = new PortConflictDetector(
            new FakeProcessRunner(string.Empty),
            new FakeTcpStateProvider
            {
                Listeners = [new TcpEndpointSnapshot("0.0.0.0", 4444)]
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => detector.EnsureAvailableAsync(
            "127.0.0.1",
            new PortMapping(4444, 4444),
            ownedRule: null,
            CancellationToken.None));
    }

    [Fact]
    public void TcpConnectionMonitorLogsReceivedAndClosedForwardedConnections()
    {
        var tcpStateProvider = new FakeTcpStateProvider
        {
            Connections =
            [
                new TcpConnectionSnapshot(
                    new TcpEndpointSnapshot("192.168.1.10", 4444),
                    new TcpEndpointSnapshot("192.168.1.20", 53000),
                    TcpState.Established)
            ]
        };
        var logger = new FakeLogger();
        var monitor = new TcpConnectionMonitor(tcpStateProvider, logger);

        monitor.Observe("0.0.0.0", [new PortMapping(4444, 4444)], "172.28.98.2");
        tcpStateProvider.Connections = [];
        monitor.Observe("0.0.0.0", [new PortMapping(4444, 4444)], "172.28.98.2");

        Assert.Contains(logger.Messages, message => message.Contains("Received TCP connection", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("forwarding to 172.28.98.2:4444", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("Forwarded TCP connection closed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FirewallManagerCreatesRuleWhenInspectorReportsMissing()
    {
        var runner = new SequenceProcessRunner(
            new ProcessResult(2, "WSLPORTPROXY_FIREWALL_RULE_MISSING", string.Empty),
            new ProcessResult(0, string.Empty, string.Empty));
        var manager = new NetshFirewallRuleManager(runner);

        await manager.EnsureAsync("kali-linux", 9999, dryRun: false, CancellationToken.None);

        Assert.Collection(
            runner.Commands,
            command =>
            {
                Assert.Equal("powershell.exe", command.FileName);
                Assert.DoesNotContain("exit 0 WSLPortProxyGuardian", command.Arguments, StringComparison.Ordinal);
            },
            command =>
            {
                Assert.Equal("netsh.exe", command.FileName);
                Assert.Contains("add rule", command.Arguments, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task FirewallManagerRefusesExistingNamedRule()
    {
        var runner = new SequenceProcessRunner(new ProcessResult(0, "WSLPORTPROXY_FIREWALL_RULE_EXISTS", string.Empty));
        var manager = new NetshFirewallRuleManager(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.EnsureAsync("kali-linux", 9999, dryRun: false, CancellationToken.None));

        Assert.Single(runner.Commands);
    }

    [Fact]
    public async Task FirewallManagerRefusesUnrecognizedInspectionOutput()
    {
        var runner = new SequenceProcessRunner(new ProcessResult(1, string.Empty, "no localized marker"));
        var manager = new NetshFirewallRuleManager(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.EnsureAsync("kali-linux", 9999, dryRun: false, CancellationToken.None));

        Assert.Single(runner.Commands);
    }

    [Fact]
    public async Task WslDiagnosticsWarnsWhenTargetPortHasNoListener()
    {
        var logger = new FakeLogger();
        var diagnostics = new WslForwardingDiagnostics(
            new FakeProcessRunner("""
                                  --WSLPORTPROXY-CONNECTIONS--
                                  """),
            logger);

        await diagnostics.InspectAsync(
            Options([new PortMapping(8080, 4444)]),
            "172.28.98.2",
            new Dictionary<int, int>(),
            CancellationToken.None);

        Assert.Contains(logger.Messages, message => message.Contains("has no listener", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WslDiagnosticsWarnsWhenTargetPortOnlyListensOnLoopback()
    {
        var logger = new FakeLogger();
        var diagnostics = new WslForwardingDiagnostics(
            new FakeProcessRunner("""
                                  LISTEN 0 128 127.0.0.1:4444 0.0.0.0:*
                                  --WSLPORTPROXY-CONNECTIONS--
                                  """),
            logger);

        await diagnostics.InspectAsync(
            Options([new PortMapping(8080, 4444)]),
            "172.28.98.2",
            new Dictionary<int, int>(),
            CancellationToken.None);

        Assert.Contains(logger.Messages, message => message.Contains("listening only on loopback", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WslDiagnosticsWarnsWhenWindowsHasConnectionButWslDoesNot()
    {
        var logger = new FakeLogger();
        var diagnostics = new WslForwardingDiagnostics(
            new FakeProcessRunner("""
                                  LISTEN 0 128 0.0.0.0:4444 0.0.0.0:*
                                  --WSLPORTPROXY-CONNECTIONS--
                                  """),
            logger);

        await diagnostics.InspectAsync(
            Options([new PortMapping(8080, 4444)]),
            "172.28.98.2",
            new Dictionary<int, int> { [8080] = 1 },
            CancellationToken.None);

        Assert.Contains(logger.Messages, message => message.Contains("Windows has 1 active connection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WslDiagnosticsLogsReachableListenerAndActiveWslConnection()
    {
        var logger = new FakeLogger();
        var diagnostics = new WslForwardingDiagnostics(
            new FakeProcessRunner("""
                                  LISTEN 0 128 0.0.0.0:4444 0.0.0.0:*
                                  --WSLPORTPROXY-CONNECTIONS--
                                  ESTAB 0 0 172.28.98.2:4444 172.28.96.1:53000
                                  """),
            logger);

        await diagnostics.InspectAsync(
            Options([new PortMapping(8080, 4444)]),
            "172.28.98.2",
            new Dictionary<int, int> { [8080] = 1 },
            CancellationToken.None);

        Assert.Contains(logger.Messages, message => message.Contains("has reachable listener", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains("active TCP connection", StringComparison.Ordinal));
    }

    private static CliOptions Options(IReadOnlyList<PortMapping> mappings) =>
        new(
            ShowHelp: false,
            ShowRunHelp: false,
            Error: null,
            Mappings: mappings,
            Distro: "ubuntu",
            ListenAddress: "0.0.0.0",
            IntervalSeconds: 1,
            ManageFirewall: true,
            DryRun: false);

    private sealed class FakeProcessRunner(string standardOutput, int exitCode = 0) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(exitCode, standardOutput, string.Empty));
    }

    private sealed class SequenceProcessRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<CommandRecord> Commands { get; } = [];

        public Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
        {
            Commands.Add(new CommandRecord(fileName, arguments));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record CommandRecord(string FileName, string Arguments);

    private sealed class FakeTcpStateProvider : ITcpStateProvider
    {
        public IReadOnlyList<TcpEndpointSnapshot> Listeners { get; init; } = [];
        public IReadOnlyList<TcpConnectionSnapshot> Connections { get; set; } = [];

        public IReadOnlyList<TcpEndpointSnapshot> GetActiveTcpListeners() => Listeners;
        public IReadOnlyList<TcpConnectionSnapshot> GetActiveTcpConnections() => Connections;
    }

    private sealed class FakeLogger : ILogSink
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
