using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DiscordProxyRelay.Tests;

public sealed class ProxyProtocolTests
{
    [Fact]
    public async Task HttpConnectUsesAuthorityAndRequiresSuccess()
    {
        await using var server = new TcpTestServer(async stream =>
        {
            var request = await TcpTestServer.ReadHeadersAsync(stream);
            Assert.StartsWith("CONNECT gateway.discord.gg:443 HTTP/1.1\r\n", request);
            await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray());
        });
        var endpoint = new ProxyEndpoint("127.0.0.1", server.Port, ProxyKind.Http, "US");

        await using var tunnel = await new ProxyConnector().ConnectAsync(endpoint, "gateway.discord.gg", 443, CancellationToken.None);

        Assert.NotNull(tunnel);
    }

    [Fact]
    public async Task HttpConnectRejectsNonSuccess()
    {
        await using var server = new TcpTestServer(async stream =>
        {
            _ = await TcpTestServer.ReadHeadersAsync(stream);
            await stream.WriteAsync("HTTP/1.1 407 Proxy Authentication Required\r\n\r\n"u8.ToArray());
        });
        var endpoint = new ProxyEndpoint("127.0.0.1", server.Port, ProxyKind.Http, "US");

        await Assert.ThrowsAsync<IOException>(() =>
            new ProxyConnector().ConnectAsync(endpoint, "gateway.discord.gg", 443, CancellationToken.None));
    }

    [Fact]
    public async Task Socks5NegotiatesNoAuthenticationAndUsesDomainConnect()
    {
        await using var server = new TcpTestServer(async stream =>
        {
            Assert.Equal(new byte[] { 5, 1, 0 }, await TcpTestServer.ReadExactlyAsync(stream, 3));
            await stream.WriteAsync(new byte[] { 5, 0 });
            var prefix = await TcpTestServer.ReadExactlyAsync(stream, 5);
            Assert.Equal(new byte[] { 5, 1, 0, 3, 18 }, prefix);
            Assert.Equal("gateway.discord.gg", Encoding.ASCII.GetString(await TcpTestServer.ReadExactlyAsync(stream, 18)));
            Assert.Equal(new byte[] { 1, 187 }, await TcpTestServer.ReadExactlyAsync(stream, 2));
            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 });
        });
        var endpoint = new ProxyEndpoint("127.0.0.1", server.Port, ProxyKind.Socks5, "CA");

        await using var tunnel = await new ProxyConnector().ConnectAsync(endpoint, "gateway.discord.gg", 443, CancellationToken.None);

        Assert.NotNull(tunnel);
    }

    [Fact]
    public async Task Socks5RejectsNonAsciiHostnameBeforeEncoding()
    {
        await using var server = new TcpTestServer(async stream =>
        {
            try
            {
                _ = await TcpTestServer.ReadExactlyAsync(stream, 3);
                await stream.WriteAsync(new byte[] { 5, 0 });
                var prefix = await TcpTestServer.ReadExactlyAsync(stream, 5);
                _ = await TcpTestServer.ReadExactlyAsync(stream, prefix[4] + 2);
                await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 });
            }
            catch (EndOfStreamException)
            {
            }
        });
        var endpoint = new ProxyEndpoint("127.0.0.1", server.Port, ProxyKind.Socks5, "US");

        await Assert.ThrowsAsync<IOException>(() =>
            new ProxyConnector().ConnectAsync(endpoint, "gáteway.discord.gg", 443, CancellationToken.None));
    }
}

internal sealed class TcpTestServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;

    internal TcpTestServer(Func<NetworkStream, Task> handler, bool repeat = false)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _run = RunAsync(handler, repeat);
    }

    internal int Port { get; }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _run; } catch (OperationCanceledException) { } catch (SocketException) { }
        _stop.Dispose();
    }

    internal static async Task<byte[]> ReadExactlyAsync(Stream stream, int count)
    {
        var result = new byte[count];
        await stream.ReadExactlyAsync(result);
        return result;
    }

    internal static async Task<string> ReadHeadersAsync(Stream stream)
    {
        var bytes = new List<byte>();
        while (bytes.Count < 16 * 1024)
        {
            var next = new byte[1];
            if (await stream.ReadAsync(next) == 0) break;
            bytes.Add(next[0]);
            if (bytes.Count >= 4 && bytes[^4..].SequenceEqual("\r\n\r\n"u8.ToArray())) break;
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private async Task RunAsync(Func<NetworkStream, Task> handler, bool repeat)
    {
        do
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            await handler(client.GetStream());
        } while (repeat && !_stop.IsCancellationRequested);
    }
}
