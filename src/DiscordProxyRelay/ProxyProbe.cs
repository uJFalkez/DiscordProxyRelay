using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace DiscordProxyRelay;

public interface IProxyConnector
{
    Task<Stream> ConnectAsync(ProxyEndpoint endpoint, string host, int port, CancellationToken cancellationToken);
}

public sealed class ProxyConnector : IProxyConnector
{
    private const int MaximumHeaderBytes = 16 * 1024;

    public async Task<Stream> ConnectAsync(ProxyEndpoint endpoint, string host, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
            var stream = client.GetStream();
            if (endpoint.Kind == ProxyKind.Http)
            {
                await EstablishHttpTunnelAsync(stream, host, port, cancellationToken);
            }
            else
            {
                await EstablishSocks5TunnelAsync(stream, host, port, cancellationToken);
            }

            return stream;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task EstablishHttpTunnelAsync(Stream stream, string host, int port, CancellationToken cancellationToken)
    {
        var authority = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]:{port}" : $"{host}:{port}";
        var request = Encoding.ASCII.GetBytes($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\nProxy-Connection: Keep-Alive\r\n\r\n");
        await stream.WriteAsync(request, cancellationToken);
        var headers = await ReadHeadersAsync(stream, cancellationToken);
        var firstLineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);
        var firstLine = firstLineEnd >= 0 ? headers[..firstLineEnd] : headers;
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], out var status) || status is < 200 or >= 300)
        {
            throw new IOException();
        }
    }

    private static async Task EstablishSocks5TunnelAsync(Stream stream, string host, int port, CancellationToken cancellationToken)
    {
        if (host.Length is 0 or > 255 || host.Any(character => character > 127))
        {
            throw new IOException();
        }

        await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken);
        var greeting = new byte[2];
        await stream.ReadExactlyAsync(greeting, cancellationToken);
        if (greeting[0] != 5 || greeting[1] != 0)
        {
            throw new IOException();
        }

        var hostBytes = Encoding.ASCII.GetBytes(host);
        var request = new byte[7 + hostBytes.Length];
        request[0] = 5;
        request[1] = 1;
        request[2] = 0;
        request[3] = 3;
        request[4] = (byte)hostBytes.Length;
        hostBytes.CopyTo(request, 5);
        request[^2] = (byte)(port >> 8);
        request[^1] = (byte)port;
        await stream.WriteAsync(request, cancellationToken);

        var responsePrefix = new byte[4];
        await stream.ReadExactlyAsync(responsePrefix, cancellationToken);
        if (responsePrefix[0] != 5 || responsePrefix[1] != 0 || responsePrefix[2] != 0)
        {
            throw new IOException();
        }

        var addressLength = responsePrefix[3] switch
        {
            1 => 4,
            4 => 16,
            3 => await ReadDomainLengthAsync(stream, cancellationToken),
            _ => throw new IOException(),
        };
        var remainder = new byte[addressLength + 2];
        await stream.ReadExactlyAsync(remainder, cancellationToken);
    }

    private static async Task<int> ReadDomainLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = new byte[1];
        await stream.ReadExactlyAsync(length, cancellationToken);
        if (length[0] == 0)
        {
            throw new IOException();
        }

        return length[0];
    }

    internal static async Task<string> ReadHeadersAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        while (bytes.Count < MaximumHeaderBytes)
        {
            var next = new byte[1];
            if (await stream.ReadAsync(next, cancellationToken) == 0)
            {
                throw new IOException();
            }

            bytes.Add(next[0]);
            if (bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        throw new IOException();
    }
}

public sealed class ProxyProbe
{
    private readonly IProxyConnector _connector;
    private readonly Func<Stream, string, CancellationToken, Task> _authenticateTls;

    public ProxyProbe(IProxyConnector? connector = null) : this(connector ?? new ProxyConnector(), AuthenticateTlsAsync)
    {
    }

    internal ProxyProbe(IProxyConnector connector, Func<Stream, string, CancellationToken, Task> authenticateTls)
    {
        _connector = connector;
        _authenticateTls = authenticateTls;
    }

    public async Task<ProxyEndpoint?> FindUsableAsync(
        IReadOnlyList<ProxyEndpoint> candidates,
        CancellationToken cancellationToken,
        Action<ProxyEndpoint>? onAttempt = null)
    {
        var groups = new[]
        {
            Stage(ProxyKind.Socks5, preferred: true),
            Stage(ProxyKind.Http, preferred: true),
            Stage(ProxyKind.Socks5, preferred: false),
            Stage(ProxyKind.Http, preferred: false),
        };

        foreach (var group in groups)
        {
            var selected = await ProbeGroupAsync(group);
            if (selected is not null)
            {
                return selected;
            }
        }

        return null;

        IEnumerable<ProxyEndpoint> Stage(ProxyKind kind, bool preferred) => candidates
            .Where(candidate => candidate.Kind == kind &&
                ProxyCatalog.IsApprovedCountry(candidate.CountryCode) &&
                ProxyCatalog.IsPreferredCountry(candidate.CountryCode) == preferred)
            .Take(ProxyCatalog.MaximumCandidatesPerStage);

        async Task<ProxyEndpoint?> ProbeGroupAsync(IEnumerable<ProxyEndpoint> group)
        {
            using var winnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var concurrency = new SemaphoreSlim(4, 4);
            var winner = new TaskCompletionSource<ProxyEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = group.Select(ProbeOneAsync).ToArray();
            if (tasks.Length == 0)
            {
                return null;
            }

            var all = Task.WhenAll(tasks);
            var completed = await Task.WhenAny(winner.Task, all);
            if (completed == winner.Task)
            {
                winnerCancellation.Cancel();
                try { await all; } catch (OperationCanceledException) { }
                return await winner.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return winner.Task.IsCompletedSuccessfully ? winner.Task.Result : null;

            async Task ProbeOneAsync(ProxyEndpoint endpoint)
            {
                try
                {
                    await concurrency.WaitAsync(winnerCancellation.Token);
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(winnerCancellation.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        onAttempt?.Invoke(endpoint);
                        await using var tunnel = await _connector.ConnectAsync(endpoint, "gateway.discord.gg", 443, timeout.Token);
                        await _authenticateTls(tunnel, "gateway.discord.gg", timeout.Token);
                        winner.TrySetResult(endpoint);
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }

    internal static async Task AuthenticateTlsAsync(Stream tunnel, string host, CancellationToken cancellationToken)
    {
        using var tls = new SslStream(tunnel, leaveInnerStreamOpen: true);
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cancellationToken);
    }
}
