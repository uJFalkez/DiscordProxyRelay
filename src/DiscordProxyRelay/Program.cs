using System.Text;

namespace DiscordProxyRelay;

internal sealed record LauncherOptions(bool Verbose, bool TemporaryGateway, bool DeprecatedPersistGatewaySeen);

internal static class Program
{
    internal const string Usage = "Uso: DiscordProxyRelay.exe [--verbose] [--temporary-gateway]";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!TryParseArguments(args, out var options))
        {
            Console.WriteLine(Usage);
            return 1;
        }

        var output = TextWriter.Synchronized(Console.Out);
        using var mutex = new Mutex(initiallyOwned: true, "DiscordProxyRelay.Singleton", out var createdNew);
        if (!createdNew)
        {
            Console.WriteLine("DiscordProxyRelay já está em execução.");
            return 1;
        }

        if (options.DeprecatedPersistGatewaySeen)
        {
            output.WriteLine("Aviso: --persist-gateway está obsoleto; o gateway persistente agora é o padrão.");
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return await new LauncherApp(
                LauncherDependencies.CreateDefault(output, options.Verbose, !options.TemporaryGateway),
                output).RunAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operação cancelada.");
            return 1;
        }
        catch
        {
            Console.WriteLine("Ocorreu uma falha inesperada. O Discord não foi alterado.");
            return 1;
        }
    }

    internal static bool TryParseArguments(string[] args, out LauncherOptions options)
    {
        var verbose = false;
        var verboseSeen = false;
        var temporaryGateway = false;
        var temporaryGatewaySeen = false;
        var deprecatedPersistGatewaySeen = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--verbose" when !verboseSeen:
                    verbose = true;
                    verboseSeen = true;
                    break;
                case "--temporary-gateway" when !temporaryGatewaySeen:
                    temporaryGateway = true;
                    temporaryGatewaySeen = true;
                    break;
                case "--persist-gateway" when !deprecatedPersistGatewaySeen:
                    deprecatedPersistGatewaySeen = true;
                    break;
                default:
                    options = null!;
                    return false;
            }
        }

        options = new LauncherOptions(verbose, temporaryGateway, deprecatedPersistGatewaySeen);
        return true;
    }
}
