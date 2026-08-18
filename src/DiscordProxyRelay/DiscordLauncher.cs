using System.Diagnostics;

namespace DiscordProxyRelay;

public static class DiscordLauncher
{
    public static ProcessStartInfo CreateStartInfo(string executablePath, int relayPort, bool verbose = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            RedirectStandardOutput = !verbose,
            RedirectStandardError = !verbose,
        };
        startInfo.ArgumentList.Add($"--proxy-server=http://127.0.0.1:{relayPort}");
        startInfo.ArgumentList.Add("--proxy-bypass-list=discord.media;*.discord.media");
        return startInfo;
    }

    public static bool Launch(DiscordInstallation installation, int relayPort, bool verbose = false)
    {
        try
        {
            var process = Process.Start(CreateStartInfo(installation.ExecutablePath, relayPort, verbose));
            if (process is null)
            {
                return false;
            }

            if (verbose)
            {
                process.Dispose();
            }
            else
            {
                _ = DrainAndDisposeAsync(process);
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    private static async Task DrainAndDisposeAsync(Process process)
    {
        using (process)
        {
            try
            {
                await Task.WhenAll(
                    process.StandardOutput.BaseStream.CopyToAsync(Stream.Null),
                    process.StandardError.BaseStream.CopyToAsync(Stream.Null));
            }
            catch
            {
            }
        }
    }
}
