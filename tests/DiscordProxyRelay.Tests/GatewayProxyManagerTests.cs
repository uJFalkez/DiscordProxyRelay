namespace DiscordProxyRelay.Tests;

public sealed class GatewayProxyManagerTests
{
    [Fact]
    public async Task FailureSuccessFailureDoesNotRotate()
    {
        var endpoint = new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US");
        var outcomes = new Queue<Func<Stream>>([
            () => throw new IOException(),
            () => new MemoryStream(),
            () => throw new IOException(),
        ]);
        var connector = new DelegateConnector((_, _, _, _) => Task.FromResult(outcomes.Dequeue()()));
        var catalogCalls = 0;
        var manager = new GatewayProxyManager(
            endpoint,
            connector,
            _ =>
            {
                Interlocked.Increment(ref catalogCalls);
                return Task.FromResult<IReadOnlyList<ProxyEndpoint>>([]);
            },
            (_, _) => Task.FromResult<ProxyEndpoint?>(null),
            _ => { },
            CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync("gateway.discord.gg", 443, CancellationToken.None));
        await using (await manager.ConnectAsync("gateway.discord.gg", 443, CancellationToken.None)) { }
        await Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync("gateway.discord.gg", 443, CancellationToken.None));
        await manager.WaitForRotationAsync();

        Assert.Equal(0, Volatile.Read(ref catalogCalls));
    }

    [Fact]
    public async Task SecondConsecutiveFailureStartsOneReplacementSearchAndFutureConnectsUseReplacement()
    {
        var failed = new ProxyEndpoint("failed.test", 8080, ProxyKind.Http, "US");
        var replacement = new ProxyEndpoint("replacement.test", 1080, ProxyKind.Socks5, "CA");
        var connector = new DelegateConnector((endpoint, _, _, _) =>
            endpoint == failed
                ? Task.FromException<Stream>(new IOException())
                : Task.FromResult<Stream>(new MemoryStream()));
        var catalogCalls = 0;
        var statuses = new List<string>();
        IReadOnlyList<ProxyEndpoint>? probed = null;
        var manager = new GatewayProxyManager(
            failed,
            connector,
            _ =>
            {
                Interlocked.Increment(ref catalogCalls);
                return Task.FromResult<IReadOnlyList<ProxyEndpoint>>([failed, replacement]);
            },
            (candidates, _) =>
            {
                probed = candidates;
                return Task.FromResult<ProxyEndpoint?>(replacement);
            },
            statuses.Add,
            CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync("gateway.discord.gg", 443, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync("gateway.discord.gg", 443, CancellationToken.None));
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await using (await manager.ConnectAsync("gateway.discord.gg", 443, CancellationToken.None)) { }

        Assert.Equal(1, Volatile.Read(ref catalogCalls));
        Assert.Equal([replacement], probed);
        Assert.Equal([failed, failed, replacement], connector.Endpoints);
        Assert.Equal([
            "Proxy do gateway falhou duas vezes. Buscando substituto...",
            $"Proxy do gateway alterado: {replacement.DisplayValue}"
        ], statuses);
    }

    [Fact]
    public async Task CatalogExcludesFailedEndpointIdentityIgnoringCountryAndHostCase()
    {
        var failed = new ProxyEndpoint("failed.test", 8080, ProxyKind.Http, "US");
        var duplicate = new ProxyEndpoint("FAILED.TEST", 8080, ProxyKind.Http, "DE");
        var replacement = new ProxyEndpoint("replacement.test", 8080, ProxyKind.Http, "CA");
        IReadOnlyList<ProxyEndpoint>? probed = null;
        var manager = new GatewayProxyManager(
            failed,
            new DelegateConnector((_, _, _, _) => Task.FromException<Stream>(new IOException())),
            _ => Task.FromResult<IReadOnlyList<ProxyEndpoint>>([duplicate, replacement]),
            (candidates, _) =>
            {
                probed = candidates;
                return Task.FromResult<ProxyEndpoint?>(replacement);
            },
            _ => { },
            CancellationToken.None);

        await FailTwiceAsync(manager);
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([replacement], probed);
    }

    [Fact]
    public async Task CatalogExcludesFailedEndpointWithEquivalentIpAddressText()
    {
        var failed = new ProxyEndpoint("2001:db8::1", 8080, ProxyKind.Http, "US");
        var duplicate = new ProxyEndpoint("2001:0DB8:0:0:0:0:0:1", 8080, ProxyKind.Http, "DE");
        var replacement = new ProxyEndpoint("2001:db8::2", 8080, ProxyKind.Http, "CA");
        IReadOnlyList<ProxyEndpoint>? probed = null;
        var manager = new GatewayProxyManager(
            failed,
            new DelegateConnector((_, _, _, _) => Task.FromException<Stream>(new IOException())),
            _ => Task.FromResult<IReadOnlyList<ProxyEndpoint>>([duplicate, replacement]),
            (candidates, _) =>
            {
                probed = candidates;
                return Task.FromResult<ProxyEndpoint?>(replacement);
            },
            _ => { },
            CancellationToken.None);

        await FailTwiceAsync(manager);
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([replacement], probed);
    }

    [Fact]
    public async Task RelayShutdownCancellationDoesNotCountButActiveCancellationDoes()
    {
        using var lifetime = new CancellationTokenSource();
        var endpoint = new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US");
        var replacement = endpoint with { Host = "replacement.test" };
        var calls = 0;
        var connector = new DelegateConnector((_, _, _, _) => ++calls switch
        {
            1 => Task.FromException<Stream>(new OperationCanceledException()),
            2 => Task.FromException<Stream>(new IOException()),
            _ => Task.FromException<Stream>(new OperationCanceledException()),
        });
        var catalogCalls = 0;
        var statuses = new List<string>();
        var manager = new GatewayProxyManager(
            endpoint, connector,
            _ =>
            {
                Interlocked.Increment(ref catalogCalls);
                return Task.FromResult<IReadOnlyList<ProxyEndpoint>>([replacement]);
            },
            (candidates, _) => Task.FromResult<ProxyEndpoint?>(candidates.Single()),
            statuses.Add, lifetime.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.ConnectAsync("gateway.discord.gg", 443, default));
        await Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync("gateway.discord.gg", 443, default));
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));
        lifetime.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.ConnectAsync("gateway.discord.gg", 443, default));
        await Assert.ThrowsAsync<OperationCanceledException>(() => manager.ConnectAsync("gateway.discord.gg", 443, default));

        Assert.Equal(1, Volatile.Read(ref catalogCalls));
        Assert.Equal([
            "Proxy do gateway falhou duas vezes. Buscando substituto...",
            $"Proxy do gateway alterado: {replacement.DisplayValue}"
        ], statuses);
    }

    [Fact]
    public async Task FailedSearchRetainsEndpointAndResetsFailures()
    {
        var endpoint = new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US");
        var connector = new DelegateConnector((_, _, _, _) => Task.FromException<Stream>(new IOException()));
        var catalogCalls = 0;
        var statuses = new List<string>();
        var manager = new GatewayProxyManager(
            endpoint, connector,
            _ => Task.FromResult<IReadOnlyList<ProxyEndpoint>>([]),
            (_, _) =>
            {
                Interlocked.Increment(ref catalogCalls);
                return Task.FromResult<ProxyEndpoint?>(null);
            },
            statuses.Add, CancellationToken.None);

        await FailTwiceAsync(manager);
        await manager.WaitForRotationAsync();
        await FailTwiceAsync(manager);
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, Volatile.Read(ref catalogCalls));
        Assert.All(connector.Endpoints, used => Assert.Equal(endpoint, used));
        Assert.Equal([
            "Proxy do gateway falhou duas vezes. Buscando substituto...",
            "Nenhum proxy substituto foi encontrado. Proxy atual mantido.",
            "Proxy do gateway falhou duas vezes. Buscando substituto...",
            "Nenhum proxy substituto foi encontrado. Proxy atual mantido."
        ], statuses);
    }

    [Fact]
    public async Task SearchErrorRetainsCurrentProxyAndReportsFailure()
    {
        var endpoint = new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US");
        var connector = new DelegateConnector((_, _, _, _) => Task.FromException<Stream>(new IOException()));
        var statuses = new List<string>();
        var manager = new GatewayProxyManager(
            endpoint,
            connector,
            _ => Task.FromException<IReadOnlyList<ProxyEndpoint>>(new HttpRequestException()),
            (_, _) => throw new InvalidOperationException(),
            statuses.Add,
            CancellationToken.None);

        await FailTwiceAsync(manager);
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([
            "Proxy do gateway falhou duas vezes. Buscando substituto...",
            "Nenhum proxy substituto foi encontrado. Proxy atual mantido."
        ], statuses);
        Assert.All(connector.Endpoints, used => Assert.Equal(endpoint, used));
    }

    [Fact]
    public async Task StatusCallbackExceptionsDoNotInterruptRotation()
    {
        var failed = new ProxyEndpoint("failed.test", 8080, ProxyKind.Http, "US");
        var replacement = failed with { Host = "replacement.test" };
        var connector = new DelegateConnector((endpoint, _, _, _) =>
            endpoint == failed
                ? Task.FromException<Stream>(new IOException())
                : Task.FromResult<Stream>(new MemoryStream()));
        var manager = new GatewayProxyManager(
            failed,
            connector,
            _ => Task.FromResult<IReadOnlyList<ProxyEndpoint>>([replacement]),
            (candidates, _) => Task.FromResult<ProxyEndpoint?>(candidates.Single()),
            _ => throw new InvalidOperationException(),
            CancellationToken.None);

        await FailTwiceAsync(manager);
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await using var stream = await manager.ConnectAsync("gateway.discord.gg", 443, default);

        Assert.Equal([failed, failed, replacement], connector.Endpoints);
    }

    [Fact]
    public async Task WaitForRotationCompletesAfterFinalStatusOutput()
    {
        var failed = new ProxyEndpoint("failed.test", 8080, ProxyKind.Http, "US");
        var replacement = failed with { Host = "replacement.test" };
        var finalStatusStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinalStatus = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new GatewayProxyManager(
            failed,
            new DelegateConnector((_, _, _, _) => Task.FromException<Stream>(new IOException())),
            _ => Task.FromResult<IReadOnlyList<ProxyEndpoint>>([replacement]),
            (candidates, _) => Task.FromResult<ProxyEndpoint?>(candidates.Single()),
            message =>
            {
                if (message.StartsWith("Proxy do gateway alterado:", StringComparison.Ordinal))
                {
                    finalStatusStarted.TrySetResult();
                    releaseFinalStatus.Task.GetAwaiter().GetResult();
                }
            },
            CancellationToken.None);

        await FailTwiceAsync(manager);
        await finalStatusStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var rotation = manager.WaitForRotationAsync();

        Assert.False(rotation.IsCompleted);
        releaseFinalStatus.TrySetResult();
        await rotation.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RelayShutdownDuringSearchDoesNotReportRetainedProxy()
    {
        using var lifetime = new CancellationTokenSource();
        var endpoint = new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US");
        var connector = new DelegateConnector((_, _, _, _) => Task.FromException<Stream>(new IOException()));
        var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statuses = new List<string>();
        var manager = new GatewayProxyManager(
            endpoint,
            connector,
            async cancellationToken =>
            {
                searchStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return [];
            },
            (_, _) => Task.FromResult<ProxyEndpoint?>(null),
            statuses.Add,
            lifetime.Token);

        await FailTwiceAsync(manager);
        await searchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        lifetime.Cancel();
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["Proxy do gateway falhou duas vezes. Buscando substituto..."], statuses);
    }

    [Fact]
    public async Task OverlappingFailuresStartOnlyOneSearch()
    {
        var endpoint = new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US");
        var replacement = endpoint with { Host = "replacement.test" };
        var connector = new DelegateConnector((_, _, _, _) => Task.FromException<Stream>(new IOException()));
        var releaseSearch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var searchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogCalls = 0;
        var manager = new GatewayProxyManager(
            endpoint, connector,
            async _ =>
            {
                Interlocked.Increment(ref catalogCalls);
                searchStarted.TrySetResult();
                await releaseSearch.Task;
                return [replacement];
            },
            (candidates, _) => Task.FromResult<ProxyEndpoint?>(candidates.Single()),
            _ => { }, CancellationToken.None);

        var failures = Enumerable.Range(0, 4)
            .Select(_ => manager.ConnectAsync("gateway.discord.gg", 443, default)).ToArray();
        foreach (var failure in failures) await Assert.ThrowsAsync<IOException>(() => failure);
        await searchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, Volatile.Read(ref catalogCalls));
        releaseSearch.TrySetResult();
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, Volatile.Read(ref catalogCalls));
    }

    [Fact]
    public async Task FailureOnPublishedReplacementSurvivesRotationCleanup()
    {
        var failed = new ProxyEndpoint("failed.test", 8080, ProxyKind.Http, "US");
        var replacement = failed with { Host = "replacement.test" };
        var connector = new DelegateConnector((_, _, _, _) => Task.FromException<Stream>(new IOException()));
        var secondSearch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GatewayProxyManager? manager = null;
        var catalogCalls = 0;
        manager = new GatewayProxyManager(
            failed, connector,
            _ =>
            {
                if (Interlocked.Increment(ref catalogCalls) == 2) secondSearch.TrySetResult();
                return Task.FromResult<IReadOnlyList<ProxyEndpoint>>([replacement]);
            },
            (candidates, _) => Task.FromResult(candidates.FirstOrDefault()),
            message =>
            {
                if (message.StartsWith("Proxy do gateway alterado:", StringComparison.Ordinal))
                {
                    Assert.Throws<IOException>(() =>
                        manager!.ConnectAsync("gateway.discord.gg", 443, default).GetAwaiter().GetResult());
                }
            },
            CancellationToken.None);

        await FailTwiceAsync(manager);
        await manager.WaitForRotationAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync("gateway.discord.gg", 443, default));

        await secondSearch.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task FailTwiceAsync(GatewayProxyManager manager)
    {
        await Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync("gateway.discord.gg", 443, default));
        await Assert.ThrowsAsync<IOException>(() => manager.ConnectAsync("gateway.discord.gg", 443, default));
    }

    private sealed class DelegateConnector(
        Func<ProxyEndpoint, string, int, CancellationToken, Task<Stream>> connect) : IProxyConnector
    {
        internal List<ProxyEndpoint> Endpoints { get; } = [];

        public Task<Stream> ConnectAsync(ProxyEndpoint endpoint, string host, int port, CancellationToken cancellationToken)
        {
            Endpoints.Add(endpoint);
            return connect(endpoint, host, port, cancellationToken);
        }
    }
}
