namespace DiscordProxyRelay.Tests;

public sealed class DiscordLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"discord-proxy-relay-{Guid.NewGuid():N}");

    [Fact]
    public void FindStableChoosesNewestNumericVersionWithExecutable()
    {
        CreateInstall("1.0.9");
        var expected = CreateInstall("1.0.10");
        CreateInstall("invalid");
        CreateInstall("2.0.0", includeExecutable: false);
        CreateInstall("9.0.0", product: "DiscordCanary");

        var installation = DiscordLocator.FindStable(_root);

        Assert.NotNull(installation);
        Assert.Equal(new Version(1, 0, 10), installation.Version);
        Assert.Equal(Path.Combine(expected, "Discord.exe"), installation.ExecutablePath);
    }

    [Fact]
    public void FindStableFailsSafelyForMissingOrInvalidRoot()
    {
        Assert.Null(DiscordLocator.FindStable(Path.Combine(_root, "missing")));
        Assert.Null(DiscordLocator.FindStable("\0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public void FindStableRejectsUntrustedLocalAppDataPath(string? localAppData)
    {
        Assert.Null(DiscordLocator.FindStable(localAppData!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private string CreateInstall(string version, bool includeExecutable = true, string product = "Discord")
    {
        var app = Path.Combine(_root, product, $"app-{version}");
        Directory.CreateDirectory(app);
        if (includeExecutable)
        {
            File.WriteAllText(Path.Combine(app, "Discord.exe"), "test");
        }

        return app;
    }
}
