using System.Text;

namespace DiscordProxyRelay;

internal sealed record LauncherOptions(bool Verbose, bool PersistGateway);

internal static class Program
{
    internal const string Usage = "Uso: DiscordProxyRelay.exe [--verbose] [--persist-gateway]";

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

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return await new LauncherApp(
                LauncherDependencies.CreateDefault(output, options.Verbose, options.PersistGateway),
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
        var persistGateway = false;
        var persistGatewaySeen = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--verbose" when !verboseSeen:
                    verbose = true;
                    verboseSeen = true;
                    break;
                case "--persist-gateway" when !persistGatewaySeen:
                    persistGateway = true;
                    persistGatewaySeen = true;
                    break;
                default:
                    options = null!;
                    return false;
            }
        }

        options = new LauncherOptions(verbose, persistGateway);
        return true;
    }
}
