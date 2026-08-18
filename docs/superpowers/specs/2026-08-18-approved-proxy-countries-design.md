# Approved Proxy Countries Design

## Goal

Release DiscordProxyRelay 1.0.1 with a strict country allowlist that prioritizes United States and Canada and never falls back to an unapproved country.

## Country Policy

The preferred country tier is:

- `US`
- `CA`

The secondary approved tier is:

- `GB`
- `IE`
- `DE`
- `FR`
- `NL`
- `BE`
- `LU`
- `CH`
- `AT`
- `DK`
- `NO`
- `SE`
- `FI`
- `IS`
- `AU`
- `NZ`
- `JP`
- `SG`

Country codes are matched case-insensitively after conversion to uppercase. Missing, empty, malformed, unknown, Brazilian, and otherwise unapproved country codes are rejected. There is no fallback outside the allowlist.

This policy improves geographic predictability. It does not claim that a public proxy is trustworthy or safe.

## Candidate Selection

The catalog keeps the existing requirements for supported protocol, valid host and port, `alive`, TLS support, finite non-negative quality metrics, and endpoint deduplication.

Candidates are divided into four stages:

1. Preferred-country SOCKS5.
2. Preferred-country HTTP CONNECT.
3. Secondary-country SOCKS5.
4. Secondary-country HTTP CONNECT.

Each stage contains at most six candidates, preserving the existing overall maximum of 24 attempts. Within each stage, candidates are ranked by uptime descending, average timeout ascending, and current timeout ascending.

## Probe Behavior

Stages are probed sequentially. A later stage starts only after every candidate in the current stage fails. Up to four candidates within one stage may be probed concurrently, and the first successful candidate in that stage wins.

This guarantees that secondary countries cannot win while a preferred-country stage is still active. It also makes US/CA HTTP candidates take precedence over secondary-country SOCKS5 candidates, as explicitly selected for this release.

## Failure Behavior

If the catalog returns no approved candidates, or all four stages fail, Discord is not started. The launcher reports that no usable proxy from the approved countries was found.

Network, JSON, and probe errors continue to produce the same safe failure outcome. Cancellation behavior remains unchanged.

## Change Surface

- `ProxyCatalog.cs`: define the two country tiers, filter unapproved countries, rank candidates, and retain up to six per stage.
- `ProxyProbe.cs`: probe the four stages in strict order.
- `LauncherApp.cs`: clarify the no-approved-proxy message.
- `ProxyCatalogTests.cs`: cover the allowlist, normalization, stage caps, and quality ranking.
- `ProxyProbeTests.cs`: cover strict stage ordering and fallback.
- `LauncherAppTests.cs`: update the expected failure message.
- `README.md`: document the exact allowlist, priority, and security caveat.
- `DiscordProxyRelay.csproj`: set product metadata to `1.0.1` and `1.0.1.0`.

No changes are required in the relay, Discord launcher, RTC bypass, proxy protocols, or build script.

## Verification And Release

Implementation follows test-driven development: new policy and ordering tests must fail before production changes and pass afterward. The complete test suite and `scripts/build-proxy-relay.sh` must pass. The Windows x64 executable and SHA-256 checksum must be regenerated for Release `v1.0.1`.
