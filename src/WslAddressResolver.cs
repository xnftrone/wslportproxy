using System.Text.RegularExpressions;

namespace WslPortProxyGuardian;

public interface IWslAddressResolver
{
    Task<string> GetPrimaryAddressAsync(string distroName, CancellationToken cancellationToken);
}

public sealed class WslAddressResolver(IProcessRunner processRunner) : IWslAddressResolver
{
    private static readonly Regex Ipv4Pattern = new(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);

    public async Task<string> GetPrimaryAddressAsync(string distroName, CancellationToken cancellationToken)
    {
        var arguments = ProcessArgumentBuilder.Join("-d", distroName, "--", "hostname", "-I");
        var result = await processRunner.RunAsync("wsl.exe", arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to resolve WSL IP for distro '{distroName}': {result.StandardError}{result.StandardOutput}".Trim());
        }

        return ParseAddress(result.StandardOutput);
    }

    public static string ParseAddress(string text)
    {
        var match = Ipv4Pattern.Match(text);
        if (!match.Success)
        {
            throw new InvalidOperationException("No IPv4 address found in WSL output.");
        }

        return match.Value;
    }
}
