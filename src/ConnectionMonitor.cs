using System.Net.NetworkInformation;

namespace WslPortProxyGuardian;

public interface IConnectionMonitor
{
    int ActiveConnectionCount { get; }
    IReadOnlyDictionary<int, int> ActiveConnectionCountsByListenPort { get; }
    void Observe(string listenAddress, IReadOnlyList<PortMapping> mappings, string connectAddress);
}

public sealed class NullConnectionMonitor : IConnectionMonitor
{
    public static NullConnectionMonitor Instance { get; } = new();

    public int ActiveConnectionCount => 0;

    public IReadOnlyDictionary<int, int> ActiveConnectionCountsByListenPort { get; } = new Dictionary<int, int>();

    public void Observe(string listenAddress, IReadOnlyList<PortMapping> mappings, string connectAddress)
    {
    }
}

public sealed class TcpConnectionMonitor(ITcpStateProvider tcpStateProvider, ILogSink logSink) : IConnectionMonitor
{
    private readonly Dictionary<ConnectionKey, string> _activeConnections = [];
    private readonly Dictionary<int, int> _activeConnectionCountsByListenPort = [];

    public int ActiveConnectionCount => _activeConnections.Count;
    public IReadOnlyDictionary<int, int> ActiveConnectionCountsByListenPort => _activeConnectionCountsByListenPort;

    public void Observe(string listenAddress, IReadOnlyList<PortMapping> mappings, string connectAddress)
    {
        var mappingsByListenPort = mappings
            .GroupBy(mapping => mapping.ListenPort)
            .ToDictionary(group => group.Key, group => group.First());

        var observed = tcpStateProvider.GetActiveTcpConnections()
            .Where(connection => IsForwardedConnection(connection, listenAddress, mappingsByListenPort))
            .Select(connection => ToObservedConnection(connection, mappingsByListenPort[connection.LocalEndPoint.Port], connectAddress))
            .ToDictionary(connection => connection.Key, connection => connection.Description);

        foreach (var connection in observed)
        {
            if (!_activeConnections.ContainsKey(connection.Key))
            {
                logSink.Info($"Received TCP connection {connection.Value}");
            }
        }

        foreach (var connection in _activeConnections)
        {
            if (!observed.ContainsKey(connection.Key))
            {
                logSink.Info($"Forwarded TCP connection closed {connection.Value}");
            }
        }

        _activeConnections.Clear();
        _activeConnectionCountsByListenPort.Clear();
        foreach (var connection in observed)
        {
            _activeConnections[connection.Key] = connection.Value;
            _activeConnectionCountsByListenPort[connection.Key.LocalPort] =
                _activeConnectionCountsByListenPort.GetValueOrDefault(connection.Key.LocalPort) + 1;
        }
    }

    private static bool IsForwardedConnection(
        TcpConnectionSnapshot connection,
        string listenAddress,
        IReadOnlyDictionary<int, PortMapping> mappingsByListenPort)
    {
        return mappingsByListenPort.ContainsKey(connection.LocalEndPoint.Port)
            && ListenAddressMatcher.Matches(listenAddress, connection.LocalEndPoint.Address)
            && IsActiveForwardingState(connection.State);
    }

    private static bool IsActiveForwardingState(TcpState state) =>
        state is TcpState.SynReceived
            or TcpState.Established
            or TcpState.FinWait1
            or TcpState.FinWait2
            or TcpState.CloseWait
            or TcpState.Closing
            or TcpState.LastAck;

    private static ObservedConnection ToObservedConnection(
        TcpConnectionSnapshot connection,
        PortMapping mapping,
        string connectAddress)
    {
        var key = new ConnectionKey(
            connection.LocalEndPoint.Address,
            connection.LocalEndPoint.Port,
            connection.RemoteEndPoint.Address,
            connection.RemoteEndPoint.Port,
            connectAddress,
            mapping.ConnectPort);
        var description = $"from {connection.RemoteEndPoint} to {connection.LocalEndPoint}; forwarding to {connectAddress}:{mapping.ConnectPort} ({connection.State}).";
        return new ObservedConnection(key, description);
    }

    private sealed record ConnectionKey(
        string LocalAddress,
        int LocalPort,
        string RemoteAddress,
        int RemotePort,
        string ConnectAddress,
        int ConnectPort);

    private sealed record ObservedConnection(ConnectionKey Key, string Description);
}
