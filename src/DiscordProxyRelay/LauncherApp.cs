namespace DiscordProxyRelay;

internal sealed record LauncherDependencies(
    bool IsWindows,
    Func<string> GetLocalAppData,
    Func<string, DiscordInstallation?> Locate,
    Func<DiscordProcessState> InspectDiscord,
    Func<CancellationToken, Task> WaitForDiscordExit,
    Func<CancellationToken, Task<IReadOnlyList<ProxyEndpoint>>> Fetch,
    Func<IReadOnlyList<ProxyEndpoint>, CancellationToken, Task<ProxyEndpoint?>> Probe,
    Func<ProxyEndpoint, CancellationToken, Task<IRelay>> StartRelay,
    Func<DiscordInstallation, int, bool> Launch,
    TimeSpan GatewayWaitDelay,
    Func<TimeSpan, CancellationToken, Task> Delay,
    Action HideConsole,
    Func<CancellationToken, Task> MonitorDiscord)
{
    internal static LauncherDependencies CreateDefault(TextWriter output, bool verbose, TimeSpan gatewayWaitDelay)
    {
        var connector = new ProxyConnector();
        var probe = new ProxyProbe(connector);
        return new LauncherDependencies(
            OperatingSystem.IsWindows(),
            () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DiscordLocator.FindStable,
            DiscordProcessMonitor.Inspect,
            DiscordProcessMonitor.WaitUntilStoppedAsync,
            ProxyCatalog.FetchAsync,
            (candidates, cancellationToken) => probe.FindUsableAsync(
                candidates,
                cancellationToken,
                endpoint => output.WriteLine($"Testando {endpoint.DisplayValue}...")),
            async (endpoint, _) =>
                await RelayServer.StartAsync(endpoint, connector, cancellationToken: CancellationToken.None),
            (installation, relayPort) => DiscordLauncher.Launch(installation, relayPort, verbose),
            gatewayWaitDelay,
            Task.Delay,
            verbose ? () => { } : ConsoleWindow.Hide,
            DiscordProcessMonitor.WaitUntilStoppedAsync);
    }
}

internal sealed class LauncherApp(LauncherDependencies dependencies, TextWriter output)
{
    internal async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!dependencies.IsWindows)
        {
            output.WriteLine("Este programa funciona somente no Windows.");
            return 1;
        }

        var localAppData = dependencies.GetLocalAppData();
        var installation = dependencies.Locate(localAppData);
        if (installation is null)
        {
            output.WriteLine("Discord Stable não foi encontrado.");
            return 1;
        }

        var processState = dependencies.InspectDiscord();
        if (processState == DiscordProcessState.Unknown)
        {
            output.WriteLine("Não foi possível verificar se o Discord está em execução. Inicialização cancelada.");
            return 1;
        }

        if (processState == DiscordProcessState.Running)
        {
            output.WriteLine("Feche o Discord. Aguardando o encerramento...");
            await dependencies.WaitForDiscordExit(cancellationToken);
            if (dependencies.InspectDiscord() != DiscordProcessState.Stopped)
            {
                output.WriteLine("Não foi possível confirmar o encerramento do Discord. Inicialização cancelada.");
                return 1;
            }
        }

        output.WriteLine("Aviso: proxies públicos podem ser instáveis e não são confiáveis.");
        output.WriteLine("Buscando proxies públicos...");
        IReadOnlyList<ProxyEndpoint> candidates;
        ProxyEndpoint? selected;
        try
        {
            candidates = await dependencies.Fetch(cancellationToken);
            selected = candidates.Count == 0 ? null : await dependencies.Probe(candidates, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            selected = null;
        }

        if (selected is null)
        {
            output.WriteLine("Nenhum proxy utilizável dos países aprovados foi encontrado. O Discord não será iniciado.");
            return 1;
        }

        output.WriteLine($"Proxy selecionado: {selected.DisplayValue}");

        cancellationToken.ThrowIfCancellationRequested();
        installation = dependencies.Locate(localAppData);
        if (installation is null)
        {
            output.WriteLine("A instalação do Discord mudou. Inicialização cancelada.");
            return 1;
        }

        output.WriteLine("Proxy validado. Iniciando relay local...");
        IRelay relay;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            relay = await dependencies.StartRelay(selected, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            output.WriteLine("Não foi possível iniciar o relay local.");
            return 1;
        }

        await using (relay)
        {
            processState = dependencies.InspectDiscord();
            if (processState != DiscordProcessState.Stopped)
            {
                output.WriteLine("O estado do Discord mudou. Inicialização cancelada.");
                return 1;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!dependencies.Launch(installation, relay.Port))
            {
                output.WriteLine("Não foi possível iniciar o Discord.");
                return 1;
            }

            var runtimeToken = CancellationToken.None;
            output.WriteLine("Discord iniciado. Aguardando conexão ao gateway...");
            var gatewayObserved = relay.GatewayObserved;
            var timeout = dependencies.Delay(TimeSpan.FromSeconds(60), runtimeToken);
            var observed = await Task.WhenAny(gatewayObserved, timeout) == gatewayObserved;
            if (!observed)
            {
                await relay.SwitchToDirectAsync();
                output.WriteLine("Gateway não observado. Relay alterado para conexão direta.");
                await dependencies.Delay(TimeSpan.FromSeconds(5), runtimeToken);
                dependencies.HideConsole();
                await dependencies.MonitorDiscord(runtimeToken);
                return 1;
            }

            output.WriteLine($"Gateway observado pelo proxy. Aguardando {dependencies.GatewayWaitDelay.TotalSeconds:0} segundos antes da troca definitiva...");
            await dependencies.Delay(dependencies.GatewayWaitDelay, runtimeToken);
            await relay.SwitchToDirectAsync();
            output.WriteLine("Troca concluída. Novas conexões são diretas.");
            await dependencies.Delay(TimeSpan.FromSeconds(5), runtimeToken);
            dependencies.HideConsole();
            await dependencies.MonitorDiscord(runtimeToken);
            return 0;
        }
    }
}
