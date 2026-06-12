using System.Net;

namespace WslPortProxyGuardian;

public interface IForwardingDiagnostics
{
    Task InspectAsync(
        CliOptions options,
        string connectAddress,
        IReadOnlyDictionary<int, int> activeConnectionsByListenPort,
        CancellationToken cancellationToken);
}

public sealed class NullForwardingDiagnostics : IForwardingDiagnostics
{
    public static NullForwardingDiagnostics Instance { get; } = new();

    public Task InspectAsync(
        CliOptions options,
        string connectAddress,
        IReadOnlyDictionary<int, int> activeConnectionsByListenPort,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class WslForwardingDiagnostics(IProcessRunner processRunner, ILogSink logSink) : IForwardingDiagnostics
{
    private const string SectionMarker = "--WSLPORTPROXY-CONNECTIONS--";

    public async Task InspectAsync(
        CliOptions options,
        string connectAddress,
        IReadOnlyDictionary<int, int> activeConnectionsByListenPort,
        CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotAsync(options.Distro, cancellationToken);
        foreach (var mapping in options.Mappings)
        {
            LogMappingStatus(options, mapping, connectAddress, activeConnectionsByListenPort, snapshot);
        }
    }

    private async Task<WslTcpSnapshot> ReadSnapshotAsync(string distroName, CancellationToken cancellationToken)
    {
        const string command =
            "if command -v ss >/dev/null 2>&1; then ss -H -ltnp; printf '\\n--WSLPORTPROXY-CONNECTIONS--\\n'; ss -H -tnp; else printf 'ss command not found\\n' >&2; exit 127; fi";
        var arguments = ProcessArgumentBuilder.Join("-d", distroName, "--", "sh", "-lc", command);
        var result = await processRunner.RunAsync("wsl.exe", arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to inspect WSL TCP state for distro '{distroName}': {result.StandardError}{result.StandardOutput}".Trim());
        }

        return WslTcpSnapshot.Parse(result.StandardOutput, SectionMarker);
    }

    private void LogMappingStatus(
        CliOptions options,
        PortMapping mapping,
        string connectAddress,
        IReadOnlyDictionary<int, int> activeConnectionsByListenPort,
        WslTcpSnapshot snapshot)
    {
        var listeners = snapshot.Listeners
            .Where(listener => listener.LocalEndPoint.Port == mapping.ConnectPort)
            .ToArray();
        var reachableListeners = listeners
            .Where(listener => ListenerAcceptsConnectAddress(listener.LocalEndPoint.Address, connectAddress))
            .ToArray();
        var connections = snapshot.Connections
            .Where(connection => connection.LocalEndPoint.Port == mapping.ConnectPort)
            .ToArray();
        activeConnectionsByListenPort.TryGetValue(mapping.ListenPort, out var windowsConnectionCount);

        if (listeners.Length == 0)
        {
            logSink.Warn($"Diagnostics: WSL target TCP {connectAddress}:{mapping.ConnectPort} has no listener. Host port {options.ListenAddress}:{mapping.ListenPort} can accept traffic, but nothing is ready inside WSL.");
        }
        else if (reachableListeners.Length == 0 && listeners.All(listener => IsLoopback(listener.LocalEndPoint.Address)))
        {
            logSink.Warn($"Diagnostics: WSL target TCP {mapping.ConnectPort} is listening only on loopback ({FormatListeners(listeners)}). Portproxy connects to {connectAddress}:{mapping.ConnectPort}, so forwarded traffic may miss that service.");
        }
        else if (reachableListeners.Length == 0)
        {
            logSink.Warn($"Diagnostics: WSL target TCP {mapping.ConnectPort} listener(s) do not include {connectAddress} or a wildcard bind: {FormatListeners(listeners)}.");
        }
        else
        {
            logSink.Info($"Diagnostics: WSL target TCP {connectAddress}:{mapping.ConnectPort} has reachable listener(s): {FormatListeners(reachableListeners)}.");
        }

        if (windowsConnectionCount > 0 && connections.Length == 0)
        {
            logSink.Warn($"Diagnostics: Windows has {windowsConnectionCount} active connection(s) on host port {mapping.ListenPort}, but WSL shows no active TCP connection on target port {mapping.ConnectPort}.");
        }
        else if (connections.Length > 0)
        {
            logSink.Info($"Diagnostics: WSL shows {connections.Length} active TCP connection(s) on target port {mapping.ConnectPort}: {FormatConnections(connections)}.");
        }
    }

    private static bool ListenerAcceptsConnectAddress(string listenerAddress, string connectAddress)
    {
        if (listenerAddress is "*" or "0.0.0.0" or "::")
        {
            return true;
        }

        return IPAddress.TryParse(listenerAddress, out var listenerIp)
            && IPAddress.TryParse(connectAddress, out var connectIp)
            && EqualityComparer<IPAddress>.Default.Equals(listenerIp, connectIp);
    }

    private static bool IsLoopback(string address)
    {
        return IPAddress.TryParse(address, out var parsed) && IPAddress.IsLoopback(parsed);
    }

    private static string FormatListeners(IReadOnlyList<WslTcpListenerSnapshot> listeners) =>
        string.Join(", ", listeners.Take(5).Select(listener => FormatEndpointWithProcess(listener.LocalEndPoint, listener.Details)));

    private static string FormatConnections(IReadOnlyList<WslTcpConnectionSnapshot> connections) =>
        string.Join(", ", connections.Take(5).Select(connection => $"{connection.LocalEndPoint}<->{connection.RemoteEndPoint} {connection.State} {ExtractProcessDetails(connection.Details)}".TrimEnd()));

    private static string FormatEndpointWithProcess(TcpEndpointSnapshot endpoint, string details)
    {
        var processDetails = ExtractProcessDetails(details);
        return string.IsNullOrWhiteSpace(processDetails)
            ? endpoint.ToString()
            : $"{endpoint} {processDetails}";
    }

    private static string ExtractProcessDetails(string details)
    {
        var usersIndex = details.IndexOf("users:", StringComparison.Ordinal);
        return usersIndex < 0 ? string.Empty : details[usersIndex..];
    }
}

public sealed record WslTcpListenerSnapshot(TcpEndpointSnapshot LocalEndPoint, string Details);

public sealed record WslTcpConnectionSnapshot(
    TcpEndpointSnapshot LocalEndPoint,
    TcpEndpointSnapshot RemoteEndPoint,
    string State,
    string Details);

public sealed record WslTcpSnapshot(
    IReadOnlyList<WslTcpListenerSnapshot> Listeners,
    IReadOnlyList<WslTcpConnectionSnapshot> Connections)
{
    public static WslTcpSnapshot Parse(string text, string sectionMarker)
    {
        var listeners = new List<WslTcpListenerSnapshot>();
        var connections = new List<WslTcpConnectionSnapshot>();
        var inConnections = false;

        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(line, sectionMarker, StringComparison.Ordinal))
            {
                inConnections = true;
                continue;
            }

            var endpoints = ParseEndpoints(line);
            if (!inConnections)
            {
                if (endpoints.Count > 0)
                {
                    listeners.Add(new WslTcpListenerSnapshot(endpoints[0], line));
                }

                continue;
            }

            if (endpoints.Count >= 2)
            {
                var state = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "UNKNOWN";
                connections.Add(new WslTcpConnectionSnapshot(endpoints[0], endpoints[1], state, line));
            }
        }

        return new WslTcpSnapshot(listeners, connections);
    }

    private static IReadOnlyList<TcpEndpointSnapshot> ParseEndpoints(string line)
    {
        var endpoints = new List<TcpEndpointSnapshot>();
        foreach (var token in line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseEndpoint(token, out var endpoint))
            {
                endpoints.Add(endpoint);
            }
        }

        return endpoints;
    }

    private static bool TryParseEndpoint(string token, out TcpEndpointSnapshot endpoint)
    {
        endpoint = new TcpEndpointSnapshot(string.Empty, 0);
        var value = token.Trim('"');
        string address;
        string portText;

        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var closeBracket = value.IndexOf(']');
            if (closeBracket < 0 || closeBracket + 1 >= value.Length || value[closeBracket + 1] != ':')
            {
                return false;
            }

            address = value[1..closeBracket];
            portText = value[(closeBracket + 2)..];
        }
        else
        {
            var delimiter = value.LastIndexOf(':');
            if (delimiter <= 0 || delimiter == value.Length - 1)
            {
                return false;
            }

            address = value[..delimiter];
            portText = value[(delimiter + 1)..];
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            return false;
        }

        endpoint = new TcpEndpointSnapshot(address, port);
        return true;
    }
}
