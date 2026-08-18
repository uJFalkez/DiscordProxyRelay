using System.Security;

namespace DiscordProxyRelay;

public sealed record DiscordInstallation(Version Version, string AppDirectory, string ExecutablePath);

public static class DiscordLocator
{
    public static DiscordInstallation? FindStable(string localAppData)
    {
        if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathFullyQualified(localAppData))
        {
            return null;
        }

        try
        {
            var root = Path.Combine(localAppData, "Discord");
            if (!Directory.Exists(root))
            {
                return null;
            }

            DiscordInstallation? newest = null;
            foreach (var directory in Directory.EnumerateDirectories(root, "app-*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (name.Length <= 4 || !Version.TryParse(name[4..], out var version))
                {
                    continue;
                }

                var executable = Path.Combine(directory, "Discord.exe");
                if (!File.Exists(executable) || newest is not null && version <= newest.Version)
                {
                    continue;
                }

                newest = new DiscordInstallation(version, directory, executable);
            }

            return newest;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or SecurityException)
        {
            return null;
        }
    }
}
