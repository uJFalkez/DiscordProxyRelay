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
    public void ParseAcceptsOnlyApprovedCountriesAndNormalizesCodes()
    {
        const string json = """
            {"proxies":[
              {"ip":"10.0.1.1","port":3001,"protocol":"http","alive":true,"ssl":true,"uptime":99,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"us"}},
              {"ip":"10.0.1.2","port":3002,"protocol":"http","alive":true,"ssl":true,"uptime":98,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"CA"}},
              {"ip":"10.0.1.3","port":3003,"protocol":"http","alive":true,"ssl":true,"uptime":97,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"DE"}},
              {"ip":"10.0.1.4","port":3004,"protocol":"http","alive":true,"ssl":true,"uptime":96,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"JP"}},
              {"ip":"10.0.1.5","port":3005,"protocol":"http","alive":true,"ssl":true,"uptime":95,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"BR"}},
              {"ip":"10.0.1.6","port":3006,"protocol":"http","alive":true,"ssl":true,"uptime":94,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"KZ"}},
              {"ip":"10.0.1.7","port":3007,"protocol":"http","alive":true,"ssl":true,"uptime":93,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"CN"}},
              {"ip":"10.0.1.8","port":3008,"protocol":"http","alive":true,"ssl":true,"uptime":92,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"RU"}},
              {"ip":"10.0.1.9","port":3009,"protocol":"http","alive":true,"ssl":true,"uptime":91,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"ZZ"}},
              {"ip":"10.0.1.10","port":3010,"protocol":"http","alive":true,"ssl":true,"uptime":90,"average_timeout":100,"timeout":70,"ip_data":{}},
              {"ip":"10.0.1.11","port":3011,"protocol":"http","alive":true,"ssl":true,"uptime":89,"average_timeout":100,"timeout":70,"ip_data":{"countryCode":"   "}}
            ]}
            """;

        var proxies = ProxyCatalog.Parse(json);

        Assert.Equal(new[] { 3001, 3002, 3003, 3004 }, proxies.Select(proxy => proxy.Port));
        Assert.Equal(new[] { "US", "CA", "DE", "JP" }, proxies.Select(proxy => proxy.CountryCode));
    }

    [Fact]
    public void ParseRanksPreferredAndSecondaryStagesByQuality()
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
            new[] { 1003, 1001, 2003, 2001, 1004, 1002, 2004, 2002 },
            proxies.Select(proxy => proxy.Port));
    }

    [Fact]
    public void ParseAppliesSixCandidateCapSeparatelyToEachStage()
    {
        var entries = Enumerable.Range(1, 7)
            .SelectMany(number => new[]
            {
                $$$"""{"ip":"10.0.1.{{{number}}}","port":{{{1000 + number}}},"protocol":"socks5","alive":true,"ssl":true,"uptime":{{{100 - number}}},"average_timeout":100,"timeout":100,"ip_data":{"countryCode":"US"}}""",
                $$$"""{"ip":"10.0.2.{{{number}}}","port":{{{2000 + number}}},"protocol":"http","alive":true,"ssl":true,"uptime":{{{100 - number}}},"average_timeout":100,"timeout":100,"ip_data":{"countryCode":"CA"}}""",
                $$$"""{"ip":"10.0.3.{{{number}}}","port":{{{3000 + number}}},"protocol":"socks5","alive":true,"ssl":true,"uptime":{{{50 - number}}},"average_timeout":100,"timeout":100,"ip_data":{"countryCode":"DE"}}""",
                $$$"""{"ip":"10.0.4.{{{number}}}","port":{{{4000 + number}}},"protocol":"http","alive":true,"ssl":true,"uptime":{{{50 - number}}},"average_timeout":100,"timeout":100,"ip_data":{"countryCode":"FR"}}""",
            });
        var json = $$$"""{"proxies":[{{{string.Join(',', entries)}}}]}""";

        var proxies = ProxyCatalog.Parse(json, 20);

        Assert.Equal(24, proxies.Count);
        Assert.Equal(6, proxies.Count(proxy => proxy.Kind == ProxyKind.Socks5 && proxy.CountryCode == "US"));
        Assert.Equal(6, proxies.Count(proxy => proxy.Kind == ProxyKind.Http && proxy.CountryCode == "CA"));
        Assert.Equal(6, proxies.Count(proxy => proxy.Kind == ProxyKind.Socks5 && proxy.CountryCode == "DE"));
        Assert.Equal(6, proxies.Count(proxy => proxy.Kind == ProxyKind.Http && proxy.CountryCode == "FR"));
        Assert.DoesNotContain(proxies, proxy => proxy.Port is 1007 or 2007 or 3007 or 4007);
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
