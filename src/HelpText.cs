namespace WslPortProxyGuardian;

public static class HelpText
{
    public static string Render(bool runHelp)
    {
        return runHelp
            ? """
              Usage:
                wslportproxy run -p 4444 -p 8000 -p 8443:443 [options]

              Options:
                -p, --port <port>            Port or port mapping. Repeatable. Supports 4444 or 8443:443 and comma lists.
                -d, --distro <name>          WSL distro name. Default: kali-linux
                -l, --listen-address <addr>  Host listen address. Default: 0.0.0.0
                -i, --interval <seconds>     Reconcile interval in seconds. Default: 3
                    --no-firewall            Do not create or remove Windows Firewall rules.
                    --dry-run                Log actions without changing system state.
                -h, --help                   Show this help.
              """
            : """
              Usage:
                wslportproxy run -p 4444 -p 8000 -p 8443:443 [options]
                wslportproxy -h | --help

              Description:
                Maintains host-to-WSL TCP portproxy mappings for a running WSL distro.
                Automatically tracks WSL IP changes, logs actions, and removes only its owned
                mappings and firewall rules when the process exits.
              """;
    }
}
