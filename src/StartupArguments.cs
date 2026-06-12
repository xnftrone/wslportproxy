namespace WslPortProxyGuardian;

public sealed record StartupArguments(IReadOnlyList<string> PublicArgs, string? LogFilePath)
{
    public const string LogFileOption = "--_wslportproxy-log-file";

    public static StartupArguments Parse(string[] args)
    {
        var publicArgs = new List<string>();
        string? logFilePath = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], LogFileOption, StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for internal option {LogFileOption}.");
                }

                logFilePath = args[i + 1];
                i++;
                continue;
            }

            publicArgs.Add(args[i]);
        }

        return new StartupArguments(publicArgs, logFilePath);
    }
}
