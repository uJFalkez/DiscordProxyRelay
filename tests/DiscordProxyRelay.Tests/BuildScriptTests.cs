namespace DiscordProxyRelay.Tests;

public sealed class BuildScriptTests
{
    [Fact]
    public void BuildScriptUsesDedicatedArtifactDirectoryOnly()
    {
        var root = FindWorkspaceRoot();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "build-proxy-relay.sh"));

        Assert.Contains("proxy-relay", script);
        Assert.Contains("Release artifact: artifacts/proxy-relay/win-x64/DiscordProxyRelay.exe", script);
        Assert.DoesNotContain("output_dir=\"$artifacts_parent/win-x64\"", script);
        Assert.DoesNotContain("Release artifact: artifacts/win-x64/DiscordProxyRelay.exe", script);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "build-proxy-relay.sh")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
