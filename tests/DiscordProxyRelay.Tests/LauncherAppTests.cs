namespace DiscordProxyRelay.Tests;

public sealed class LauncherAppTests
{
    private static readonly DiscordInstallation Installation = new(new Version(1, 0), "app", "Discord.exe");
    private static readonly ProxyEndpoint Endpoint = new("proxy.test", 80, ProxyKind.Http, "US");

    [Fact]
    public async Task InitiallyRunningDiscordIsWaitedForBeforeProxyFetch()
    {
        var events = new List<string>();
        var inspections = new Queue<DiscordProcessState>([DiscordProcessState.Running, DiscordProcessState.Stopped]);
        var dependencies = CreateDependencies(
            inspect: () => inspections.TryDequeue(out var state) ? state : DiscordProcessState.Stopped,
            waitForExit: _ => { events.Add("wait"); return Task.CompletedTask; },
            fetch: _ => { events.Add("fetch"); return Task.FromResult<IReadOnlyList<ProxyEndpoint>>([]); });

        var exitCode = await new LauncherApp(dependencies, new StringWriter()).RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(["wait", "fetch"], events);
    }

    [Fact]
    public async Task UnknownDiscordStateAbortsBeforeNetworkOrLaunch()
    {
        var fetched = false;
        var launched = false;
        var output = new StringWriter();
        var dependencies = CreateDependencies(
            inspect: () => DiscordProcessState.Unknown,
            fetch: _ => { fetched = true; return Task.FromResult<IReadOnlyList<ProxyEndpoint>>([]); },
            launch: (_, _) => { launched = true; return true; });

        var exitCode = await new LauncherApp(dependencies, output).RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(fetched);
        Assert.False(launched);
        Assert.Contains("Não foi possível verificar se o Discord está em execução.", output.ToString());
    }

    [Fact]
    public async Task NoUsableProxyDoesNotLaunchDiscord()
    {
        var launched = false;
        var output = new StringWriter();
        var dependencies = CreateDependencies(
            probe: (_, _) => Task.FromResult<ProxyEndpoint?>(null),
            launch: (_, _) => { launched = true; return true; });

        var exitCode = await new LauncherApp(dependencies, output).RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(launched);
        Assert.Contains("Nenhum proxy utilizável dos países aprovados foi encontrado. O Discord não será iniciado.", output.ToString());
        Assert.DoesNotContain("proxy.test", output.ToString());
    }

    [Fact]
    public async Task LaunchFailureDisposesRelay()
    {
        var events = new List<string>();
        var relay = new FakeRelay(events, Task.CompletedTask);
        var dependencies = CreateDependencies(
            relay: relay,
            launch: (_, _) => { events.Add("launch"); return false; });

        var exitCode = await new LauncherApp(dependencies, new StringWriter()).RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(["launch", "dispose"], events);
    }

    [Fact]
    public async Task GatewaySuccessUsesRequiredOrderingAndDisposesRelay()
    {
        var events = new List<string>();
        var delays = new List<TimeSpan>();
        var relay = new FakeRelay(events, Task.CompletedTask);
        var dependencies = CreateDependencies(
            relay: relay,
            launch: (_, _) => { events.Add("launch"); return true; },
            delay: (duration, cancellationToken) =>
            {
                delays.Add(duration);
                if (delays.Count == 1)
                {
                    return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                events.Add($"delay:{duration.TotalSeconds:0}");
                return Task.CompletedTask;
            },
            hide: () => events.Add("hide"),
            monitor: _ => { events.Add("monitor"); return Task.CompletedTask; },
            gatewayWaitDelay: TimeSpan.FromSeconds(60));

        var exitCode = await new LauncherApp(dependencies, new StringWriter()).RunAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal([TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5)], delays);
        Assert.Equal(["launch", "delay:60", "switch", "delay:5", "hide", "monitor", "dispose"], events);
    }

    [Fact]
    public async Task GatewayTimeoutSwitchesDirectThenHidesAndDisposesRelay()
    {
        var events = new List<string>();
        var relay = new FakeRelay(events, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task);
        var dependencies = CreateDependencies(
            relay: relay,
            launch: (_, _) => { events.Add("launch"); return true; },
            delay: (duration, _) => { events.Add($"delay:{duration.TotalSeconds:0}"); return Task.CompletedTask; },
            hide: () => events.Add("hide"),
            monitor: _ => { events.Add("monitor"); return Task.CompletedTask; });

        var exitCode = await new LauncherApp(dependencies, new StringWriter()).RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(["launch", "delay:60", "switch", "delay:5", "hide", "monitor", "dispose"], events);
    }

    [Fact]
    public async Task CancellationBeforeRelayCreationDoesNotStartRelayOrLaunch()
    {
        using var cancellation = new CancellationTokenSource();
        var relayStarted = false;
        var launched = false;
        var dependencies = CreateDependencies(
            probe: (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult<ProxyEndpoint?>(Endpoint);
            },
            startRelay: (_, _) =>
            {
                relayStarted = true;
                return Task.FromResult<IRelay>(new FakeRelay([], Task.CompletedTask));
            },
            launch: (_, _) => { launched = true; return true; });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new LauncherApp(dependencies, new StringWriter()).RunAsync(cancellation.Token));

        Assert.False(relayStarted);
        Assert.False(launched);
    }

    [Fact]
    public async Task CancellationAfterRelayCreationDisposesRelayWithoutLaunching()
    {
        using var cancellation = new CancellationTokenSource();
        var events = new List<string>();
        var relay = new FakeRelay(events, Task.CompletedTask);
        var launched = false;
        var dependencies = CreateDependencies(
            startRelay: (_, _) =>
            {
                events.Add("start-relay");
                cancellation.Cancel();
                return Task.FromResult<IRelay>(relay);
            },
            launch: (_, _) => { launched = true; return true; });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new LauncherApp(dependencies, new StringWriter()).RunAsync(cancellation.Token));

        Assert.False(launched);
        Assert.Equal(["start-relay", "dispose"], events);
    }

    [Fact]
    public async Task CancellationAfterSuccessfulLaunchDoesNotInterruptRuntimeRelay()
    {
        using var cancellation = new CancellationTokenSource();
        var events = new List<string>();
        var monitorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishMonitor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var relay = new FakeRelay(events, Task.CompletedTask);
        var dependencies = CreateDependencies(
            relay: relay,
            launch: (_, _) =>
            {
                events.Add("launch");
                cancellation.Cancel();
                return true;
            },
            delay: (duration, token) =>
            {
                Assert.False(token.IsCancellationRequested);
                return duration == TimeSpan.FromSeconds(60)
                    ? Task.Delay(Timeout.InfiniteTimeSpan, token)
                    : Task.CompletedTask;
            },
            monitor: token =>
            {
                Assert.False(token.IsCancellationRequested);
                events.Add("monitor");
                monitorEntered.TrySetResult();
                return finishMonitor.Task;
            });

        var run = new LauncherApp(dependencies, new StringWriter()).RunAsync(cancellation.Token);
        await monitorEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("switch", events);
        Assert.DoesNotContain("dispose", events);

        finishMonitor.TrySetResult();
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("dispose", events[^1]);
    }

    [Fact]
    public async Task LocatorRefreshFailureAbortsBeforeRelayCreationAndLaunch()
    {
        var locations = new Queue<DiscordInstallation?>([Installation, null]);
        var relayStarted = false;
        var launched = false;
        var output = new StringWriter();
        var dependencies = CreateDependencies(
            locate: _ => locations.Dequeue(),
            startRelay: (_, _) =>
            {
                relayStarted = true;
                return Task.FromResult<IRelay>(new FakeRelay([], Task.CompletedTask));
            },
            launch: (_, _) => { launched = true; return true; });

        var exitCode = await new LauncherApp(dependencies, output).RunAsync(CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(relayStarted);
        Assert.False(launched);
        Assert.Contains("A instalação do Discord mudou. Inicialização cancelada.", output.ToString());
    }

    private static LauncherDependencies CreateDependencies(
        Func<string, DiscordInstallation?>? locate = null,
        Func<DiscordProcessState>? inspect = null,
        Func<CancellationToken, Task>? waitForExit = null,
        Func<CancellationToken, Task<IReadOnlyList<ProxyEndpoint>>>? fetch = null,
        Func<IReadOnlyList<ProxyEndpoint>, CancellationToken, Task<ProxyEndpoint?>>? probe = null,
        FakeRelay? relay = null,
        Func<ProxyEndpoint, CancellationToken, Task<IRelay>>? startRelay = null,
        Func<DiscordInstallation, int, bool>? launch = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action? hide = null,
        Func<CancellationToken, Task>? monitor = null,
        TimeSpan? gatewayWaitDelay = null) =>
        new(
            true,
            () => Path.GetTempPath(),
            locate ?? (_ => Installation),
            inspect ?? (() => DiscordProcessState.Stopped),
            waitForExit ?? (_ => Task.CompletedTask),
            fetch ?? (_ => Task.FromResult<IReadOnlyList<ProxyEndpoint>>([Endpoint])),
            probe ?? ((_, _) => Task.FromResult<ProxyEndpoint?>(Endpoint)),
            startRelay ?? ((_, _) => Task.FromResult<IRelay>(relay ?? new FakeRelay([], Task.CompletedTask))),
            launch ?? ((_, _) => true),
            gatewayWaitDelay ?? TimeSpan.FromSeconds(10),
            delay ?? ((_, _) => Task.CompletedTask),
            hide ?? (() => { }),
            monitor ?? (_ => Task.CompletedTask));

    private sealed class FakeRelay(List<string> events, Task gatewayObserved) : IRelay
    {
        public int Port => 32123;
        public Task GatewayObserved { get; } = gatewayObserved;

        public Task SwitchToDirectAsync()
        {
            events.Add("switch");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }
}
