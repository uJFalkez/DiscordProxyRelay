using System.Globalization;
using System.Net;

namespace DiscordProxyRelay;

public enum ProxyKind
{
    Http,
    Socks5,
}

public sealed record ProxyEndpoint(string Host, int Port, ProxyKind Kind, string CountryCode)
{
    public string DisplayValue
    {
        get
        {
            var protocol = Kind == ProxyKind.Socks5 ? "SOCKS5" : "HTTP";
            var host = Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]" : Host;
            return $"{protocol} {host}:{Port} ({CountryCode})";
        }
    }
}

public readonly record struct ConnectAuthority(string Host, int Port)
{
    public string Value => Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
    public bool IsDiscordGateway => Host.Equals("gateway.discord.gg", StringComparison.OrdinalIgnoreCase);
    public bool IsDiscordMedia =>
        Host.Equals("discord.media", StringComparison.OrdinalIgnoreCase) ||
        Host.EndsWith(".discord.media", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? value, out ConnectAuthority authority)
    {
        authority = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 260 || value.IndexOfAny(['\r', '\n', '@', '/', '\\']) >= 0)
        {
            return false;
        }

        string host;
        string portText;
        if (value[0] == '[')
        {
            var closingBracket = value.IndexOf(']');
            if (closingBracket <= 1 || closingBracket + 1 >= value.Length || value[closingBracket + 1] != ':')
            {
                return false;
            }

            host = value[1..closingBracket];
            portText = value[(closingBracket + 2)..];
            if (!IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                return false;
            }
        }
        else
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || value.IndexOf(':') != separator)
            {
                return false;
            }

            host = value[..separator];
            portText = value[(separator + 1)..];
            if (host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                return false;
            }
        }

        if (host.Length is 0 or > 253 ||
            !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            return false;
        }

        var parsed = new ConnectAuthority(host, port);
        if (port != 443 && !parsed.IsDiscordMedia)
        {
            return false;
        }

        authority = parsed;
        return true;
    }
}
