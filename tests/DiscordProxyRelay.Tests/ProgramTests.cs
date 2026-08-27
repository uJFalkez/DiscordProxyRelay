namespace DiscordProxyRelay.Tests;

public sealed class ProgramTests
{
    public static TheoryData<string[], bool, bool, bool> ValidArguments => new()
    {
        { [], false, false, false },
        { ["--verbose"], true, false, false },
        { ["--temporary-gateway"], false, true, false },
        { ["--verbose", "--temporary-gateway"], true, true, false },
        { ["--temporary-gateway", "--verbose"], true, true, false },
        { ["--persist-gateway"], false, false, true },
        { ["--persist-gateway", "--temporary-gateway"], false, true, true },
        { ["--temporary-gateway", "--persist-gateway"], false, true, true }
    };

    public static TheoryData<string[]> InvalidArguments => new()
    {
        { ["--verbose", "--verbose"] },
        { ["--temporary-gateway", "--temporary-gateway"] },
        { ["--persist-gateway", "--persist-gateway"] },
        { ["--temporary-gateway=true"] },
        { ["--persist-gateway=true"] },
        { ["--persist-gateway", "true"] },
        { ["--unknown"] },
        { ["--gateway-wait-delay", "10"] }
    };

    [Theory]
    [MemberData(nameof(ValidArguments))]
    public void TryParseArgumentsAcceptsValidOptions(
        string[] args,
        bool expectedVerbose,
        bool expectedTemporaryGateway,
        bool expectedDeprecatedPersistGatewaySeen)
    {
        var parsed = Program.TryParseArguments(args, out var options);

        Assert.True(parsed);
        Assert.Equal(expectedVerbose, options.Verbose);
        Assert.Equal(expectedTemporaryGateway, options.TemporaryGateway);
        Assert.Equal(expectedDeprecatedPersistGatewaySeen, options.DeprecatedPersistGatewaySeen);
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
            "Uso: DiscordProxyRelay.exe [--verbose] [--temporary-gateway]",
            Program.Usage);
    }
}
