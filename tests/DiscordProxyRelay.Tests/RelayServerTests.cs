using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DiscordProxyRelay.Tests;

public sealed class RelayServerTests
{
    [Fact]
    public async Task RelayBindsLoopbackSignalsGatewayOnceAndHardSwitchesToDirect()
    {
        await using var target = new TcpTestServer(async stream =>
        {
            var buffer = new byte[32];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) return;
                await stream.WriteAsync(buffer.AsMemory(0, read));
            }
        }, repeat: true);
        var bootstrap = new LocalConnector(target.Port);
        var directCalls = 0;
        async Task<Stream> Direct(string host, int port, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref directCalls);
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, target.Port, cancellationToken);
            return client.GetStream();
        }
        await using var relay = await RelayServer.StartAsync(
            new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US"), bootstrap, Direct, CancellationToken.None);

        Assert.Equal(IPAddress.Loopback, relay.LocalEndpoint.Address);
        var gatewaySignals = 0;
        _ = relay.GatewayObserved.ContinueWith(
            _ => Interlocked.Increment(ref gatewaySignals),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        var bootstrapClient = await ConnectThroughRelayAsync(relay.Port, "gateway.discord.gg:443");
        var secondBootstrapClient = await ConnectThroughRelayAsync(relay.Port, "gateway.discord.gg:443");
        await relay.GatewayObserved.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, gatewaySignals);
        await bootstrapClient.GetStream().WriteAsync("before"u8.ToArray());
        Assert.Equal("before", await ReadTextAsync(bootstrapClient.GetStream(), 6));

        await relay.SwitchToDirectAsync();

        var closed = await bootstrapClient.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, closed);
        var secondClosed = await secondBootstrapClient.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, secondClosed);
        bootstrapClient.Dispose();
        secondBootstrapClient.Dispose();
        using var directClient = await ConnectThroughRelayAsync(relay.Port, "cdn.discordapp.com:443");
        await directClient.GetStream().WriteAsync("after"u8.ToArray());
        Assert.Equal("after", await ReadTextAsync(directClient.GetStream(), 5));
        Assert.Equal(1, directCalls);
        Assert.Equal(2, bootstrap.Calls.Count);
    }

    [Fact]
    public async Task RelayRejectsNonConnectAndNon443Targets()
    {
        await using var relay = await RelayServer.StartAsync(
            new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US"),
            new LocalConnector(1),
            (_, _, _) => throw new InvalidOperationException(),
            CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, relay.Port);
        await client.GetStream().WriteAsync("CONNECT example.com:80 HTTP/1.1\r\n\r\n"u8.ToArray());

        var response = await TcpTestServer.ReadHeadersAsync(client.GetStream());

        Assert.StartsWith("HTTP/1.1 400", response);
    }

    [Fact]
    public async Task DiscordMediaUsesDirectConnectorDuringBootstrapAndSurvivesHardSwitch()
    {
        await using var target = new TcpTestServer(async stream =>
        {
            var buffer = new byte[32];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) return;
                await stream.WriteAsync(buffer.AsMemory(0, read));
            }
        }, repeat: true);
        var bootstrap = new LocalConnector(target.Port);
        var directCalls = 0;
        async Task<Stream> Direct(string host, int port, CancellationToken cancellationToken)
        {
            Assert.Equal("c-gru.discord.media", host);
            Assert.Equal(8443, port);
            Interlocked.Increment(ref directCalls);
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, target.Port, cancellationToken);
            return client.GetStream();
        }
        await using var relay = await RelayServer.StartAsync(
            new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US"), bootstrap, Direct, CancellationToken.None);
        using var client = await ConnectThroughRelayAsync(relay.Port, "c-gru.discord.media:8443");
        await client.GetStream().WriteAsync("before"u8.ToArray());
        Assert.Equal("before", await ReadTextAsync(client.GetStream(), 6));

        await relay.SwitchToDirectAsync();

        await client.GetStream().WriteAsync("after"u8.ToArray());
        Assert.Equal("after", await ReadTextAsync(client.GetStream(), 5));
        Assert.Equal(1, directCalls);
        Assert.Empty(bootstrap.Calls);
    }

    [Fact]
    public async Task RelayRejectsClientsBeyondActiveLimit()
    {
        await using var relay = await RelayServer.StartAsync(
            new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US"),
            new HoldingConnector(),
            (_, _, _) => Task.FromResult<Stream>(new HoldingStream()),
            CancellationToken.None);
        var clients = new List<TcpClient>();
        try
        {
            for (var index = 0; index < RelayServer.MaximumActiveClients; index++)
            {
                clients.Add(await ConnectThroughRelayAsync(relay.Port, "gateway.discord.gg:443"));
            }

            using var excess = new TcpClient();
            await excess.ConnectAsync(IPAddress.Loopback, relay.Port);
            await excess.GetStream().WriteAsync("CONNECT gateway.discord.gg:443 HTTP/1.1\r\n\r\n"u8.ToArray());

            var read = await excess.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(0, read);
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelayTimesOutOnlyConnectionEstablishment(bool directMode)
    {
        var timeoutObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Stream> Hang(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException();
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested) timeoutObserved.TrySetResult();
            }
        }
        var connector = new DelegateConnector((_, _, _, cancellationToken) => Hang(cancellationToken));
        await using var relay = await RelayServer.StartAsync(
            new ProxyEndpoint("proxy.test", 8080, ProxyKind.Http, "US"),
            connector,
            (_, _, cancellationToken) => Hang(cancellationToken),
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);
        if (directMode) await relay.SwitchToDirectAsync();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, relay.Port);
        await client.GetStream().WriteAsync("CONNECT gateway.discord.gg:443 HTTP/1.1\r\n\r\n"u8.ToArray());

        var response = await TcpTestServer.ReadHeadersAsync(client.GetStream()).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.StartsWith("HTTP/1.1 502", response);
        await timeoutObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task<TcpClient> ConnectThroughRelayAsync(int port, string authority)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n\r\n"));
        var response = await TcpTestServer.ReadHeadersAsync(client.GetStream());
        Assert.StartsWith("HTTP/1.1 200", response);
        return client;
    }

    private static async Task<string> ReadTextAsync(Stream stream, int count) =>
        Encoding.ASCII.GetString(await TcpTestServer.ReadExactlyAsync(stream, count));

    private sealed class LocalConnector(int targetPort) : IProxyConnector
    {
        internal ConcurrentBag<string> Calls { get; } = [];

        public async Task<Stream> ConnectAsync(ProxyEndpoint endpoint, string host, int port, CancellationToken cancellationToken)
        {
            Calls.Add(host);
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, targetPort, cancellationToken);
            return client.GetStream();
        }
    }

    private sealed class HoldingConnector : IProxyConnector
    {
        public Task<Stream> ConnectAsync(ProxyEndpoint endpoint, string host, int port, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new HoldingStream());
    }

    private sealed class DelegateConnector(
        Func<ProxyEndpoint, string, int, CancellationToken, Task<Stream>> connect) : IProxyConnector
    {
        public Task<Stream> ConnectAsync(ProxyEndpoint endpoint, string host, int port, CancellationToken cancellationToken) =>
            connect(endpoint, host, port, cancellationToken);
    }

    private sealed class HoldingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) { }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(WaitForCancellationAsync(cancellationToken));
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
