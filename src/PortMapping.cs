namespace WslPortProxyGuardian;

public sealed record PortMapping(int ListenPort, int ConnectPort)
{
    public override string ToString() => ListenPort == ConnectPort
        ? ListenPort.ToString()
        : $"{ListenPort}:{ConnectPort}";
}
