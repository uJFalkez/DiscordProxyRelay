namespace DiscordProxyRelay.Tests;

public sealed class ProgramTests
{
    public static TheoryData<string[], bool, bool> ValidArguments => new()
    {
        { [], false, false },
        { ["--verbose"], true, false },
        { ["--persist-gateway"], false, true },
        { ["--verbose", "--persist-gateway"], true, true },
        { ["--persist-gateway", "--verbose"], true, true }
    };

    public static TheoryData<string[]> InvalidArguments => new()
    {
        { ["--verbose", "--verbose"] },
        { ["--persist-gateway", "--persist-gateway"] },
        { ["--persist-gateway=true"] },
        { ["--persist-gateway", "true"] },
        { ["--unknown"] },
        { ["--gateway-wait-delay", "10"] }
    };

    [Theory]
    [MemberData(nameof(ValidArguments))]
    public void TryParseArgumentsAcceptsValidOptions(string[] args, bool expectedVerbose, bool expectedPersistGateway)
    {
        var parsed = Program.TryParseArguments(args, out var options);

        Assert.True(parsed);
        Assert.Equal(expectedVerbose, options.Verbose);
        Assert.Equal(expectedPersistGateway, options.PersistGateway);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void TryParseArgumentsRejectsInvalidOptions(string[] args)
    {
        Assert.False(Program.TryParseArguments(args, out _));
    }

    [Fact]
    public void UsageListsAllSupportedOptions()
    {
        Assert.Equal(
            "Uso: DiscordProxyRelay.exe [--verbose] [--persist-gateway]",
            Program.Usage);
    }
}
