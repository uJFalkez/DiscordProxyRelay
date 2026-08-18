using System.Text;

namespace DiscordProxyRelay;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length > 1 || args.Length == 1 && args[0] != "--verbose")
        {
            Console.WriteLine("Uso: DiscordProxyRelay.exe [--verbose]");
            return 1;
        }

        var verbose = args.Length == 1;
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
                LauncherDependencies.CreateDefault(output, verbose),
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
}
