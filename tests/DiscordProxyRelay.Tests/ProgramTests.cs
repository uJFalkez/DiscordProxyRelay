namespace DiscordProxyRelay.Tests;

public sealed class ProgramTests
{
    public static TheoryData<string[], bool, int> ValidArguments => new()
    {
        { [], false, 10 },
        { ["--verbose"], true, 10 },
        { ["--gateway-wait-delay", "30"], false, 30 },
        { ["--verbose", "--gateway-wait-delay", "30"], true, 30 },
        { ["--gateway-wait-delay", "30", "--verbose"], true, 30 },
        { ["--gateway-wait-delay", "1"], false, 1 },
        { ["--gateway-wait-delay", "600"], false, 600 }
    };

    public static TheoryData<string[]> InvalidArguments => new()
    {
        { ["--gateway-wait-delay"] },
        { ["--gateway-wait-delay", "0"] },
        { ["--gateway-wait-delay", "601"] },
        { ["--gateway-wait-delay", "1.5"] },
        { ["--verbose", "--verbose"] },
        { ["--gateway-wait-delay", "10", "--gateway-wait-delay", "20"] },
        { ["--unknown"] }
    };

    [Theory]
    [MemberData(nameof(ValidArguments))]
    public void TryParseArgumentsAcceptsValidOptions(string[] args, bool expectedVerbose, int expectedDelaySeconds)
    {
        var parsed = Program.TryParseArguments(args, out var options);

        Assert.True(parsed);
        Assert.Equal(expectedVerbose, options.Verbose);
        Assert.Equal(TimeSpan.FromSeconds(expectedDelaySeconds), options.GatewayWaitDelay);
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
            "Uso: DiscordProxyRelay.exe [--verbose] [--gateway-wait-delay <seconds>]",
            Program.Usage);
    }
}
