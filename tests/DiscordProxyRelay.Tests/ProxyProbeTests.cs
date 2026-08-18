using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DiscordProxyRelay.Tests;

public sealed class ProxyProbeTests
{
    [Fact]
    public async Task ProbeCapsAttemptsAndConcurrencyAndReturnsTlsSuccess()
    {
        var endpoints = Enumerable.Range(1, 20)
            .Select(port => new ProxyEndpoint("proxy.test", port, ProxyKind.Http, "US"))
            .ToArray();
        var connector = new ProbeConnector();
        var active = 0;
        var maximumActive = 0;
        var probe = new ProxyProbe(connector, async (stream, _, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, current);
            try
            {
                await Task.Delay(20, cancellationToken);
                if (((TaggedStream)stream).Port != 5) throw new IOException();
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        });

        var selected = await probe.FindUsableAsync(endpoints, CancellationToken.None);

        Assert.Equal(5, selected?.Port);
        Assert.InRange(connector.Attempts.Count, 1, 12);
        Assert.InRange(maximumActive, 1, 4);
    }

    [Fact]
    public async Task ProbeRejectsTunnelWhenTlsAuthenticationFails()
    {
        var endpoint = new ProxyEndpoint("proxy.test", 1, ProxyKind.Http, "US");
        var probe = new ProxyProbe(new ProbeConnector(), (_, _, _) => throw new IOException());

        Assert.Null(await probe.FindUsableAsync([endpoint], CancellationToken.None));
    }

    [Fact]
    public async Task ProbeContainsUnexpectedCandidateFailure()
    {
        var endpoints = new[]
        {
            new ProxyEndpoint("proxy.test", 1, ProxyKind.Http, "US"),
            new ProxyEndpoint("proxy.test", 2, ProxyKind.Http, "US"),
        };
        var probe = new ProxyProbe(new ProbeConnector(), async (stream, _, cancellationToken) =>
        {
            if (((TaggedStream)stream).Port == 1) throw new InvalidOperationException();
            await Task.Delay(20, cancellationToken);
        });

        var selected = await probe.FindUsableAsync(endpoints, CancellationToken.None);

        Assert.Equal(2, selected?.Port);
    }

    [Fact]
    public async Task ProbeDoesNotAttemptHttpWhenSocks5Succeeds()
    {
        var endpoints = new[]
        {
            new ProxyEndpoint("proxy.test", 2001, ProxyKind.Http, "US"),
            new ProxyEndpoint("proxy.test", 1001, ProxyKind.Socks5, "US"),
        };
        var connector = new ProbeConnector();
        var probe = new ProxyProbe(connector, (_, _, _) => Task.CompletedTask);

        var selected = await probe.FindUsableAsync(endpoints, CancellationToken.None);

        Assert.Equal(endpoints[1], selected);
        Assert.DoesNotContain(connector.Attempts, endpoint => endpoint.Kind == ProxyKind.Http);
    }

    [Fact]
    public async Task ProbeAttemptsHttpOnlyAfterAllBoundedSocks5CandidatesFail()
    {
        var socksFailures = 0;
        var socksFailuresAtFirstHttpAttempt = -1;
        var endpoints = Enumerable.Range(1, 13)
            .Select(port => new ProxyEndpoint("proxy.test", port, ProxyKind.Socks5, "US"))
            .Append(new ProxyEndpoint("proxy.test", 2001, ProxyKind.Http, "US"))
            .ToArray();
        var connector = new ProbeConnector(endpoint =>
        {
            if (endpoint.Kind == ProxyKind.Http)
            {
                Interlocked.CompareExchange(ref socksFailuresAtFirstHttpAttempt, Volatile.Read(ref socksFailures), -1);
            }
        });
        var probe = new ProxyProbe(connector, (stream, _, _) =>
        {
            if (((TaggedStream)stream).Endpoint.Kind == ProxyKind.Socks5)
            {
                Interlocked.Increment(ref socksFailures);
                throw new IOException();
            }

            return Task.CompletedTask;
        });

        var selected = await probe.FindUsableAsync(endpoints, CancellationToken.None);

        Assert.Equal(endpoints[^1], selected);
        Assert.Equal(12, socksFailuresAtFirstHttpAttempt);
        Assert.DoesNotContain(connector.Attempts, endpoint => endpoint.Port == 13);
    }

    [Fact]
    public async Task ProductionTlsAuthenticationRejectsSelfSignedCertificate()
    {
        using var certificate = CreateSelfSignedCertificate();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var tls = new SslStream(client.GetStream());
            try
            {
                await tls.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
            }
            catch (AuthenticationException)
            {
            }
            catch (IOException)
            {
            }
        });

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port);
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            ProxyProbe.AuthenticateTlsAsync(tcpClient.GetStream(), "gateway.discord.gg", CancellationToken.None));

        listener.Stop();
        await server.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
    }

    private sealed class ProbeConnector(Action<ProxyEndpoint>? onAttempt = null) : IProxyConnector
    {
        internal ConcurrentBag<ProxyEndpoint> Attempts { get; } = [];

        public Task<Stream> ConnectAsync(ProxyEndpoint endpoint, string host, int port, CancellationToken cancellationToken)
        {
            Attempts.Add(endpoint);
            onAttempt?.Invoke(endpoint);
            return Task.FromResult<Stream>(new TaggedStream(endpoint));
        }
    }

    private sealed class TaggedStream(ProxyEndpoint endpoint) : MemoryStream
    {
        internal ProxyEndpoint Endpoint { get; } = endpoint;

        internal int Port => Endpoint.Port;
    }
}
