using System.Globalization;

namespace WslPortProxyGuardian;

public static class CliParser
{
    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return CliOptions.Help();
        }

        if (args.Any(IsHelpToken))
        {
            return args.Length > 0 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase)
                ? CliOptions.Help(runHelp: true)
                : CliOptions.Help();
        }

        if (!string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
        {
            return CliOptions.Help(error: $"Unknown command: {args[0]}");
        }

        var distro = "kali-linux";
        var listenAddress = "0.0.0.0";
        var intervalSeconds = 3;
        var manageFirewall = true;
        var dryRun = false;
        var portSpecs = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-p":
                case "--port":
                    i = RequireValue(args, i, arg, portSpecs);
                    break;
                case "-d":
                case "--distro":
                    i = RequireValue(args, i, arg, value => distro = value);
                    break;
                case "-l":
                case "--listen-address":
                    i = RequireValue(args, i, arg, value => listenAddress = value);
                    break;
                case "-i":
                case "--interval":
                    i = RequireValue(args, i, arg, value =>
                    {
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intervalSeconds) || intervalSeconds < 1)
                        {
                            throw new ArgumentException("Interval must be a positive integer.");
                        }
                    });
                    break;
                case "--no-firewall":
                    manageFirewall = false;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    return CliOptions.Help(runHelp: true, error: $"Unknown option: {arg}");
            }
        }

        try
        {
            var mappings = PortSpecParser.ParseMany(portSpecs);
            if (mappings.Count == 0)
            {
                return CliOptions.Help(runHelp: true, error: "At least one -p/--port value is required.");
            }

            return new CliOptions(false, false, null, mappings, distro, listenAddress, intervalSeconds, manageFirewall, dryRun);
        }
        catch (ArgumentException ex)
        {
            return CliOptions.Help(runHelp: true, error: ex.Message);
        }
    }

    private static bool IsHelpToken(string value) =>
        string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase);

    private static int RequireValue(string[] args, int index, string option, List<string> collector)
    {
        return RequireValue(args, index, option, value => collector.Add(value));
    }

    private static int RequireValue(string[] args, int index, string option, Action<string> setter)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {option}.");
        }

        setter(args[index + 1]);
        return index + 1;
    }
}
