namespace DiscordProxyRelay.Tests;

public sealed class ProxyCatalogTests
{
    [Fact]
    public void ApiRequestUsesDocumentedBoundedJsonQuery()
    {
        Assert.Contains("request=displayproxies", ProxyCatalog.ApiEndpoint.Query);
        Assert.Contains("limit=500", ProxyCatalog.ApiEndpoint.Query);
        Assert.Contains("format=json", ProxyCatalog.ApiEndpoint.Query);
    }

    [Fact]
    public async Task FetchRejectsBodyLargerThanOneMiB()
    {
        using var client = new HttpClient(new StaticResponseHandler(
            new ByteArrayContent(new byte[ProxyCatalog.MaximumResponseBytes + 1])));

        await Assert.ThrowsAsync<IOException>(() => ProxyCatalog.FetchAsync(client, CancellationToken.None));
    }

    [Fact]
    public void ParseRequiresSafeAvailableSslCandidatesWithValidQuality()
    {
        const string json = """
            {"proxies":[
              {"ip":"10.0.0.1","port":1001,"protocol":"http","alive":true,"ssl":true,"uptime":99.5,"average_timeout":120,"timeout":80,"ip_data":{"countryCode":"DE"}},
              {"ip":"10.0.0.2","port":1002,"protocol":"socks5","alive":true,"ssl":true,"uptime":98,"average_timeout":140,"timeout":90,"ip_data":{"countryCode":"CA"}},
              {"ip":"10.0.0.3","port":1003,"protocol":"http","alive":false,"ssl":true,"uptime":99,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.4","port":1004,"protocol":"http","alive":true,"ssl":false,"uptime":99,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.5","port":1005,"protocol":"http","alive":"true","ssl":true,"uptime":99,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.6","port":1006,"protocol":"http","alive":true,"ssl":true,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.7","port":1007,"protocol":"http","alive":true,"ssl":true,"uptime":99,"average_timeout":-1,"timeout":70,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.8","port":1008,"protocol":"http","alive":true,"ssl":true,"uptime":99,"average_timeout":100,"timeout":1e999,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.9","port":1009,"protocol":"http","alive":true,"ssl":true,"uptime":99,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"BR"}},
              {"ip":"10.0.0.10","port":1010,"protocol":"socks4","alive":true,"ssl":true,"uptime":99,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.11","port":1011,"protocol":"http","alive":true,"ssl":true,"uptime":99,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"ZZ"}},
              {"ip":"10.0.0.1","port":1001,"protocol":"http","alive":true,"ssl":true,"uptime":99.5,"average_timeout":120,"timeout":80,"ip_data":{"countryCode":"DE"}}
            ]}
            """;

        var proxies = ProxyCatalog.Parse(json);

        Assert.Collection(
            proxies,
            proxy => Assert.Equal((1002, ProxyKind.Socks5), (proxy.Port, proxy.Kind)),
            proxy => Assert.Equal((1001, ProxyKind.Http), (proxy.Port, proxy.Kind)));
    }

    [Fact]
    public void ParseRanksEachProtocolByUptimeThenAverageAndCurrentTimeout()
    {
        const string json = """
            {"proxies":[
              {"ip":"10.0.0.1","port":2001,"protocol":"http","alive":true,"ssl":true,"uptime":98,"average_timeout":100,"timeout":40,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.2","port":1001,"protocol":"socks5","alive":true,"ssl":true,"uptime":98,"average_timeout":100,"timeout":40,"ip_data":{"countryCode":"US"}},
              {"ip":"10.0.0.3","port":2002,"protocol":"http","alive":true,"ssl":true,"uptime":99,"average_timeout":300,"timeout":90,"ip_data":{"countryCode":"DE"}},
              {"ip":"10.0.0.4","port":1002,"protocol":"socks5","alive":true,"ssl":true,"uptime":99,"average_timeout":300,"timeout":90,"ip_data":{"countryCode":"DE"}},
              {"ip":"10.0.0.5","port":2003,"protocol":"http","alive":true,"ssl":true,"uptime":99,"average_timeout":200,"timeout":80,"ip_data":{"countryCode":"CA"}},
              {"ip":"10.0.0.6","port":1003,"protocol":"socks5","alive":true,"ssl":true,"uptime":99,"average_timeout":200,"timeout":80,"ip_data":{"countryCode":"CA"}},
              {"ip":"10.0.0.7","port":2004,"protocol":"http","alive":true,"ssl":true,"uptime":99,"average_timeout":200,"timeout":70,"ip_data":{"countryCode":"FR"}},
              {"ip":"10.0.0.8","port":1004,"protocol":"socks5","alive":true,"ssl":true,"uptime":99,"average_timeout":200,"timeout":70,"ip_data":{"countryCode":"FR"}}
            ]}
            """;

        var proxies = ProxyCatalog.Parse(json);

        Assert.Equal(
            new[] { 1004, 1003, 1002, 1001, 2004, 2003, 2002, 2001 },
            proxies.Select(proxy => proxy.Port));
    }

    [Fact]
    public void ParseAppliesCandidateCapSeparatelyToEachProtocol()
    {
        var entries = Enumerable.Range(1, 13)
            .SelectMany(number => new[]
            {
                $$$"""{"ip":"10.0.1.{{{number}}}","port":{{{1000 + number}}},"protocol":"socks5","alive":true,"ssl":true,"uptime":{{{100 - number}}},"average_timeout":100,"timeout":100,"ip_data":{"countryCode":"US"}}""",
                $$$"""{"ip":"10.0.2.{{{number}}}","port":{{{2000 + number}}},"protocol":"http","alive":true,"ssl":true,"uptime":{{{100 - number}}},"average_timeout":100,"timeout":100,"ip_data":{"countryCode":"US"}}""",
            });
        var json = $$$"""{"proxies":[{{{string.Join(',', entries)}}}]}""";

        var proxies = ProxyCatalog.Parse(json, 20);

        Assert.Equal(24, proxies.Count);
        Assert.Equal(12, proxies.Count(proxy => proxy.Kind == ProxyKind.Socks5));
        Assert.Equal(12, proxies.Count(proxy => proxy.Kind == ProxyKind.Http));
        Assert.DoesNotContain(proxies, proxy => proxy.Port is 1013 or 2013);
    }

    [Fact]
    public void ParseReturnsEmptyForMalformedContent()
    {
        Assert.Empty(ProxyCatalog.Parse("not json", 12));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"proxies\":[null,1,[],{}]}")]
    public void ParseReturnsEmptyForUnexpectedJsonShapes(string json)
    {
        var exception = Record.Exception(() => ProxyCatalog.Parse(json, 12));

        Assert.Null(exception);
        Assert.Empty(ProxyCatalog.Parse(json, 12));
    }

    private sealed class StaticResponseHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
    }
}
