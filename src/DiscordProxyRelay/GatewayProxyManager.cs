using System.Net;

namespace DiscordProxyRelay;

public interface IGatewayProxyConnector
{
    Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken);
}

public sealed class GatewayProxyManager : IGatewayProxyConnector
{
    private readonly object _sync = new();
    private readonly IProxyConnector _connector;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ProxyEndpoint>>> _fetchCatalog;
    private readonly Func<IReadOnlyList<ProxyEndpoint>, CancellationToken, Task<ProxyEndpoint?>> _probe;
    private readonly Action<string> _status;
    private readonly CancellationToken _relayLifetime;
    private ProxyEndpoint _endpoint;
    private int _consecutiveFailures;
    private bool _rotationInProgress;
    private Task _lastRotationTask = Task.CompletedTask;

    public GatewayProxyManager(
        ProxyEndpoint endpoint,
        IProxyConnector connector,
        Func<CancellationToken, Task<IReadOnlyList<ProxyEndpoint>>> fetchCatalog,
        Func<IReadOnlyList<ProxyEndpoint>, CancellationToken, Task<ProxyEndpoint?>> probe,
        Action<string> status,
        CancellationToken relayLifetime)
    {
        _endpoint = endpoint;
        _connector = connector;
        _fetchCatalog = fetchCatalog;
        _probe = probe;
        _status = status;
        _relayLifetime = relayLifetime;
    }

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        ProxyEndpoint endpoint;
        lock (_sync)
        {
            endpoint = _endpoint;
        }

        try
        {
            var stream = await _connector.ConnectAsync(endpoint, host, port, cancellationToken);
            lock (_sync)
            {
                if (_endpoint == endpoint)
                {
                    _consecutiveFailures = 0;
                }
            }

            return stream;
        }
        catch (OperationCanceledException) when (_relayLifetime.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            lock (_sync)
            {
                if (_endpoint == endpoint && ++_consecutiveFailures >= 2 && !_rotationInProgress)
                {
                    StartRotationLocked(endpoint);
                }
            }

            throw;
        }
    }

    internal Task WaitForRotationAsync()
    {
        lock (_sync)
        {
            return _lastRotationTask;
        }
    }

    private async Task RotateAsync(ProxyEndpoint failedEndpoint)
    {
        try
        {
            ReportStatus("Proxy do gateway falhou duas vezes. Buscando substituto...");

            ProxyEndpoint? replacement = null;
            try
            {
                var catalog = await _fetchCatalog(_relayLifetime);
                var candidates = catalog.Where(endpoint => !HasSameIdentity(endpoint, failedEndpoint)).ToArray();
                replacement = await _probe(candidates, _relayLifetime);
            }
            catch
            {
            }

            var changed = false;
            lock (_sync)
            {
                if (replacement is not null && _endpoint == failedEndpoint)
                {
                    _endpoint = replacement;
                    changed = true;
                }

                _consecutiveFailures = 0;
            }

            if (_relayLifetime.IsCancellationRequested)
            {
                return;
            }

            ReportStatus(changed
                ? $"Proxy do gateway alterado: {replacement!.DisplayValue}"
                : "Nenhum proxy substituto foi encontrado. Proxy atual mantido.");
        }
        finally
        {
            lock (_sync)
            {
                _rotationInProgress = false;
                if (_consecutiveFailures >= 2 && !_relayLifetime.IsCancellationRequested)
                {
                    StartRotationLocked(_endpoint);
                }
            }
        }
    }

    private void StartRotationLocked(ProxyEndpoint failedEndpoint)
    {
        _rotationInProgress = true;
        _lastRotationTask = Task.Run(() => RotateAsync(failedEndpoint));
    }

    private static bool HasSameIdentity(ProxyEndpoint left, ProxyEndpoint right)
    {
        if (left.Kind != right.Kind || left.Port != right.Port)
        {
            return false;
        }

        if (IPAddress.TryParse(left.Host, out var leftAddress) &&
            IPAddress.TryParse(right.Host, out var rightAddress))
        {
            return leftAddress.Equals(rightAddress);
        }

        return left.Host.Equals(right.Host, StringComparison.OrdinalIgnoreCase);
    }

    private void ReportStatus(string message)
    {
        if (_relayLifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            _status(message);
        }
        catch
        {
        }
    }
}
