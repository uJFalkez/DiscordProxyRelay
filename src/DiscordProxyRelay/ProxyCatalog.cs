using System.Text;
using System.Text.Json;

namespace DiscordProxyRelay;

public static class ProxyCatalog
{
    internal const int MaximumResponseBytes = 1024 * 1024;
    internal const int MaximumCandidatesPerStage = 6;
    internal static readonly Uri ApiEndpoint = new("https://api.proxyscrape.com/v4/free-proxy-list/get?request=displayproxies&protocol=http,socks5&format=json&timeout=20000&limit=500");
    private static readonly HashSet<string> PreferredCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "US", "CA",
    };
    private static readonly HashSet<string> SecondaryCountries = new(StringComparer.OrdinalIgnoreCase)
    {
        "GB", "IE", "DE", "FR", "NL", "BE", "LU", "CH", "AT", "DK", "NO", "SE", "FI", "IS",
        "AU", "NZ", "JP", "SG",
    };

    internal static bool IsPreferredCountry(string countryCode) =>
        PreferredCountries.Contains(countryCode);

    internal static bool IsApprovedCountry(string countryCode) =>
        IsPreferredCountry(countryCode) || SecondaryCountries.Contains(countryCode);

    public static async Task<IReadOnlyList<ProxyEndpoint>> FetchAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        return await FetchAsync(client, cancellationToken);
    }

    internal static async Task<IReadOnlyList<ProxyEndpoint>> FetchAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(ApiEndpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw new IOException();
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (body.Length + read > MaximumResponseBytes)
            {
                throw new IOException();
            }

            body.Write(buffer, 0, read);
        }

        return Parse(Encoding.UTF8.GetString(body.GetBuffer(), 0, checked((int)body.Length)), 12);
    }

    public static IReadOnlyList<ProxyEndpoint> Parse(string json, int maximumCandidates = 12)
    {
        if (maximumCandidates <= 0)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetProperty(document.RootElement, "proxies", out var proxies) || proxies.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parsed = new List<RankedProxy>();
            foreach (var item in proxies.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryReadString(item, "ip", out var host) || !TryReadInt(item, "port", out var port) ||
                    !TryReadString(item, "protocol", out var protocol) || port is < 1 or > 65535)
                {
                    continue;
                }

                ProxyKind kind;
                if (protocol.Equals("http", StringComparison.OrdinalIgnoreCase))
                {
                    kind = ProxyKind.Http;
                }
                else if (protocol.Equals("socks5", StringComparison.OrdinalIgnoreCase))
                {
                    kind = ProxyKind.Socks5;
                }
                else
                {
                    continue;
                }

                if (!TryReadBoolean(item, "alive", out var alive) || !alive ||
                    !TryReadBoolean(item, "ssl", out var ssl) || !ssl ||
                    !TryReadQuality(item, "uptime", out var uptime) ||
                    !TryReadQuality(item, "average_timeout", out var averageTimeout) ||
                    !TryReadQuality(item, "timeout", out var timeout))
                {
                    continue;
                }

                if (!TryGetProperty(item, "ip_data", out var ipData) ||
                    !TryReadString(ipData, "countryCode", out var countryCode))
                {
                    continue;
                }

                countryCode = countryCode.ToUpperInvariant();
                if (!IsApprovedCountry(countryCode) ||
                    Uri.CheckHostName(host) == UriHostNameType.Unknown ||
                    !unique.Add($"{kind}|{host}|{port}"))
                {
                    continue;
                }

                parsed.Add(new RankedProxy(
                    new ProxyEndpoint(host, port, kind, countryCode),
                    uptime,
                    averageTimeout,
                    timeout));
            }

            return Rank(ProxyKind.Socks5, preferred: true)
                .Concat(Rank(ProxyKind.Http, preferred: true))
                .Concat(Rank(ProxyKind.Socks5, preferred: false))
                .Concat(Rank(ProxyKind.Http, preferred: false))
                .Select(proxy => proxy.Endpoint)
                .ToArray();

            IEnumerable<RankedProxy> Rank(ProxyKind kind, bool preferred) => parsed
                .Where(proxy => proxy.Endpoint.Kind == kind &&
                    IsPreferredCountry(proxy.Endpoint.CountryCode) == preferred)
                .OrderByDescending(proxy => proxy.Uptime)
                .ThenBy(proxy => proxy.AverageTimeout)
                .ThenBy(proxy => proxy.Timeout)
                .Take(Math.Min(maximumCandidates, MaximumCandidatesPerStage));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        if (TryGetProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return value.Length > 0;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadInt(JsonElement element, string name, out int value)
    {
        if (TryGetProperty(element, name, out var property))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryReadBoolean(JsonElement element, string name, out bool value)
    {
        if (TryGetProperty(element, name, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryReadQuality(JsonElement element, string name, out double value)
    {
        if (TryGetProperty(element, name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out value) && double.IsFinite(value) && value >= 0)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private sealed record RankedProxy(
        ProxyEndpoint Endpoint,
        double Uptime,
        double AverageTimeout,
        double Timeout);
}
