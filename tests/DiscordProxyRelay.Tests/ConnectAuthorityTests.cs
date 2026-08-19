namespace DiscordProxyRelay.Tests;

public sealed class ConnectAuthorityTests
{
    [Theory]
    [InlineData("gateway.discord.gg", true)]
    [InlineData("GATEWAY.DISCORD.GG", true)]
    [InlineData("canary.gateway.discord.gg", false)]
    [InlineData("gateway.discord.gg.example.com", false)]
    public void IsDiscordGatewayMatchesOnlyTheExactHost(string host, bool expected)
    {
        Assert.Equal(expected, new ConnectAuthority(host, 443).IsDiscordGateway);
    }

    [Theory]
    [InlineData("gateway.discord.gg:443", "gateway.discord.gg")]
    [InlineData("127.0.0.1:443", "127.0.0.1")]
    [InlineData("[::1]:443", "::1")]
    public void TryParseAcceptsHttpsAuthorities(string input, string expectedHost)
    {
        Assert.True(ConnectAuthority.TryParse(input, out var authority));
        Assert.Equal(expectedHost, authority.Host);
        Assert.Equal(443, authority.Port);
    }

    [Theory]
    [InlineData("discord.media:1", 1)]
    [InlineData("discord.media:2053", 2053)]
    [InlineData("c-gru.discord.media:8443", 8443)]
    [InlineData("C-GRU.DISCORD.MEDIA:65535", 65535)]
    public void TryParseAcceptsAnyValidPortForDiscordMedia(string input, int expectedPort)
    {
        Assert.True(ConnectAuthority.TryParse(input, out var authority));
        Assert.True(authority.IsDiscordMedia);
        Assert.Equal(expectedPort, authority.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gateway.discord.gg")]
    [InlineData(":443")]
    [InlineData("gateway.discord.gg:80")]
    [InlineData("discord.media:0")]
    [InlineData("discord.media:65536")]
    [InlineData("discord.media:+2053")]
    [InlineData("discord.media:\t2053")]
    [InlineData("evil-discord.media:2053")]
    [InlineData("discord.media.example.com:2053")]
    [InlineData("example.com:2053")]
    [InlineData("user@gateway.discord.gg:443")]
    [InlineData("https://gateway.discord.gg:443")]
    [InlineData("gateway.discord.gg:abc")]
    [InlineData("gateway.discord.gg:443\r\nInjected: yes")]
    public void TryParseRejectsUnsafeAuthorities(string input)
    {
        Assert.False(ConnectAuthority.TryParse(input, out _));
    }

    [Fact]
    public void TryParseRejectsOverlongHost()
    {
        Assert.False(ConnectAuthority.TryParse($"{new string('a', 254)}:443", out _));
    }
}
