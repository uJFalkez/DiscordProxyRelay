using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DiscordProxyRelay;

public interface IRelay : IAsyncDisposable
{
    int Port { get; }
    Task GatewayObserved { get; }
    Task SwitchToDirectAsync();
}

public sealed class RelayServer : IRelay
{
    internal const int MaximumActiveClients = 64;
    private readonly ProxyEndpoint _proxy;
    private readonly IProxyConnector _proxyConnector;
    private readonly IGatewayProxyConnector? _gatewayProxyConnector;
    private readonly Func<string, int, CancellationToken, Task<Stream>> _directConnector;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationTokenSource _bootstrap;
    private readonly TimeSpan _connectionTimeout;
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private readonly TaskCompletionSource _gatewayObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _acceptLoop;
    private int _nextClientId;
    private int _activeClients;
    private int _mode;

    private RelayServer(
        ProxyEndpoint proxy,
        IProxyConnector proxyConnector,
        Func<string, int, CancellationToken, Task<Stream>> directConnector,
        TimeSpan connectionTimeout,
        CancellationToken cancellationToken,
        Func<CancellationToken, IGatewayProxyConnector>? gatewayProxyConnectorFactory)
    {
        _proxy = proxy;
        _proxyConnector = proxyConnector;
        _directConnector = directConnector;
        _connectionTimeout = connectionTimeout;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _bootstrap = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _gatewayProxyConnector = gatewayProxyConnectorFactory?.Invoke(_lifetime.Token);
    }

    public int Port => LocalEndpoint.Port;
    public IPEndPoint LocalEndpoint => (IPEndPoint)_listener.LocalEndpoint;
    public Task GatewayObserved => _gatewayObserved.Task;

    public static Task<RelayServer> StartAsync(
        ProxyEndpoint proxy,
        IProxyConnector? proxyConnector = null,
        Func<string, int, CancellationToken, Task<Stream>>? directConnector = null,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, IGatewayProxyConnector>? gatewayProxyConnectorFactory = null)
    {
        return StartAsync(
            proxy,
            proxyConnector ?? new ProxyConnector(),
            directConnector ?? ConnectDirectAsync,
            TimeSpan.FromSeconds(5),
            cancellationToken,
            gatewayProxyConnectorFactory);
    }

    internal static Task<RelayServer> StartAsync(
        ProxyEndpoint proxy,
        IProxyConnector proxyConnector,
        Func<string, int, CancellationToken, Task<Stream>> directConnector,
        TimeSpan connectionTimeout,
        CancellationToken cancellationToken,
        Func<CancellationToken, IGatewayProxyConnector>? gatewayProxyConnectorFactory = null)
    {
        if (connectionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout));
        }

        var relay = new RelayServer(
            proxy,
            proxyConnector,
            directConnector,
            connectionTimeout,
            cancellationToken,
            gatewayProxyConnectorFactory);
        relay._listener.Start();
        relay._acceptLoop = relay.AcceptLoopAsync();
        return Task.FromResult(relay);
    }

    public Task SwitchToDirectAsync()
    {
        if (Interlocked.Exchange(ref _mode, 1) == 0)
        {
            _bootstrap.Cancel();
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _bootstrap.Cancel();
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch (OperationCanceledException) { } catch (SocketException) { }
        }

        var clients = _clients.Values.ToArray();
        if (clients.Length > 0)
        {
            try { await Task.WhenAll(clients).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }

        _bootstrap.Dispose();
        _lifetime.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (_lifetime.IsCancellationRequested)
            {
                break;
            }

            if (Interlocked.Increment(ref _activeClients) > MaximumActiveClients)
            {
                Interlocked.Decrement(ref _activeClients);
                client.Dispose();
                continue;
            }

            var id = Interlocked.Increment(ref _nextClientId);
            var task = HandleTrackedClientAsync(client);
            _clients[id] = task;
            _ = task.ContinueWith(completedTask => _clients.TryRemove(id, out var removedTask), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task HandleTrackedClientAsync(TcpClient client)
    {
        try
        {
            await HandleClientAsync(client);
        }
        finally
        {
            Interlocked.Decrement(ref _activeClients);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            var clientStream = client.GetStream();
            ConnectAuthority authority;
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                handshake.CancelAfter(TimeSpan.FromSeconds(5));
                var headers = await ProxyConnector.ReadHeadersAsync(clientStream, handshake.Token);
                var lineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);
                var firstLine = lineEnd < 0 ? string.Empty : headers[..lineEnd];
                var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3 || !parts[0].Equals("CONNECT", StringComparison.Ordinal) ||
                    parts[2] is not ("HTTP/1.1" or "HTTP/1.0") || !ConnectAuthority.TryParse(parts[1], out authority))
                {
                    await WriteStatusAsync(clientStream, 400, handshake.Token);
                    return;
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                return;
            }

            var bootstrap = Volatile.Read(ref _mode) == 0;
            var directRtc = authority.IsDiscordMedia;
            var persistentGateway = _gatewayProxyConnector is not null && authority.IsDiscordGateway;
            var tunnelToken = bootstrap && !directRtc && !persistentGateway ? _bootstrap.Token : _lifetime.Token;
            Stream upstream;
            try
            {
                using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(tunnelToken);
                connectionCancellation.CancelAfter(_connectionTimeout);
                upstream = persistentGateway
                    ? await _gatewayProxyConnector!.ConnectAsync(authority.Host, authority.Port, connectionCancellation.Token)
                    : bootstrap && !directRtc
                        ? await _proxyConnector.ConnectAsync(_proxy, authority.Host, authority.Port, connectionCancellation.Token)
                        : await _directConnector(authority.Host, authority.Port, connectionCancellation.Token);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                try { await WriteStatusAsync(clientStream, 502, _lifetime.Token); } catch { }
                return;
            }

            await using (upstream)
            {
                if (bootstrap && authority.IsDiscordGateway)
                {
                    _gatewayObserved.TrySetResult();
                }

                try
                {
                    await clientStream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), tunnelToken);
                    using var pumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(tunnelToken);
                    using var closeClient = pumpCancellation.Token.Register(client.Dispose);
                    using var closeUpstream = pumpCancellation.Token.Register(upstream.Dispose);
                    var outgoing = clientStream.CopyToAsync(upstream, 81920, pumpCancellation.Token);
                    var incoming = upstream.CopyToAsync(clientStream, 81920, pumpCancellation.Token);
                    await Task.WhenAny(outgoing, incoming);
                    pumpCancellation.Cancel();
                    try { await Task.WhenAll(outgoing, incoming); } catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
                {
                }
            }
        }
    }

    private static Task WriteStatusAsync(Stream stream, int status, CancellationToken cancellationToken)
    {
        var text = status switch
        {
            400 => "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n",
            _ => "HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n",
        };
        return stream.WriteAsync(Encoding.ASCII.GetBytes(text), cancellationToken).AsTask();
    }

    private static async Task<Stream> ConnectDirectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken);
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
