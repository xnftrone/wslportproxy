using System.Net;
using System.Net.NetworkInformation;

namespace WslPortProxyGuardian;

public sealed record TcpEndpointSnapshot(string Address, int Port)
{
    public override string ToString() => $"{Address}:{Port}";
}

public sealed record TcpConnectionSnapshot(
    TcpEndpointSnapshot LocalEndPoint,
    TcpEndpointSnapshot RemoteEndPoint,
    TcpState State);

public interface ITcpStateProvider
{
    IReadOnlyList<TcpEndpointSnapshot> GetActiveTcpListeners();
    IReadOnlyList<TcpConnectionSnapshot> GetActiveTcpConnections();
}

public sealed class SystemTcpStateProvider : ITcpStateProvider
{
    public IReadOnlyList<TcpEndpointSnapshot> GetActiveTcpListeners()
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpListeners()
            .Select(endpoint => new TcpEndpointSnapshot(endpoint.Address.ToString(), endpoint.Port))
            .ToArray();
    }

    public IReadOnlyList<TcpConnectionSnapshot> GetActiveTcpConnections()
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveTcpConnections()
            .Select(connection => new TcpConnectionSnapshot(
                new TcpEndpointSnapshot(connection.LocalEndPoint.Address.ToString(), connection.LocalEndPoint.Port),
                new TcpEndpointSnapshot(connection.RemoteEndPoint.Address.ToString(), connection.RemoteEndPoint.Port),
                connection.State))
            .ToArray();
    }
}

public static class ListenAddressMatcher
{
    public static bool Matches(string requested, string existing)
    {
        if (string.Equals(requested, "*", StringComparison.Ordinal) ||
            string.Equals(existing, "*", StringComparison.Ordinal))
        {
            return true;
        }

        if (IsIpv4Wildcard(requested))
        {
            return IsIpv4AddressOrWildcard(existing);
        }

        if (IsIpv4Wildcard(existing))
        {
            return IsIpv4AddressOrWildcard(requested);
        }

        if (IsIpv6Wildcard(requested) || IsIpv6Wildcard(existing))
        {
            return true;
        }

        return string.Equals(requested, existing, StringComparison.OrdinalIgnoreCase)
            || IPAddressMatches(requested, existing);
    }

    private static bool IsIpv4Wildcard(string value) =>
        string.Equals(value, "0.0.0.0", StringComparison.Ordinal);

    private static bool IsIpv6Wildcard(string value) =>
        string.Equals(value, "::", StringComparison.Ordinal);

    private static bool IsIpv4AddressOrWildcard(string value)
    {
        if (IsIpv4Wildcard(value))
        {
            return true;
        }

        return IPAddress.TryParse(value, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
    }

    private static bool IPAddressMatches(string left, string right)
    {
        return IPAddress.TryParse(left, out var leftIp)
            && IPAddress.TryParse(right, out var rightIp)
            && EqualityComparer<IPAddress>.Default.Equals(leftIp, rightIp);
    }
}
