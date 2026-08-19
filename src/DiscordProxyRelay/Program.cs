using System.Globalization;
using System.Text;

namespace DiscordProxyRelay;

internal sealed record LauncherOptions(bool Verbose, TimeSpan GatewayWaitDelay);

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!TryParseArguments(args, out var options))
        {
            Console.WriteLine("Uso: DiscordProxyRelay.exe [--verbose] [--gateway-wait-delay <seconds>]");
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
                LauncherDependencies.CreateDefault(output, options.Verbose, options.GatewayWaitDelay),
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
        var gatewayWaitDelay = TimeSpan.FromSeconds(10);
        var gatewayWaitDelaySeen = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--verbose" when !verboseSeen:
                    verbose = true;
                    verboseSeen = true;
                    break;
                case "--gateway-wait-delay" when !gatewayWaitDelaySeen:
                    gatewayWaitDelaySeen = true;
                    if (++index >= args.Length
                        || !int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                        || seconds is < 1 or > 600)
                    {
                        options = null!;
                        return false;
                    }

                    gatewayWaitDelay = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    options = null!;
                    return false;
            }
        }

        options = new LauncherOptions(verbose, gatewayWaitDelay);
        return true;
    }
}
