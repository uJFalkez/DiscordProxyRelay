namespace DiscordProxyRelay.Tests;

public sealed class DiscordLauncherTests
{
    [Fact]
    public void CreateStartInfoUsesLoopbackProxyWithoutMediaBypass()
    {
        var info = DiscordLauncher.CreateStartInfo(@"C:\Local\Discord\app-1.0.0\Discord.exe", 32123);

        Assert.Equal(@"C:\Local\Discord\app-1.0.0\Discord.exe", info.FileName);
        Assert.Equal(["--proxy-server=http://127.0.0.1:32123"], info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
    }
}
