# Discord Media Control Proxy Design

## Evidence

Three successful livestream NetLogs independently showed regional hosts matching `c-*.discord.media`. The successful broadcaster and viewer runs used different dynamic hostnames, but shared the same host class and connection sequence: DNS, TCP, TLS, HTTP Upgrade, and connected WebSocket. Ports observed included 443 and 8443.

Startup baselines did not contain this host class. Regional Gateway connections appeared in transition baselines too, so they do not explain livestream success by themselves. NetLog did not provide conclusive evidence that UDP media needs foreign egress.

## Goal

Proxy the location-sensitive RTC control connections needed to start and join livestreams while keeping ongoing audio/video media traffic direct.

## Routing Policy

- Existing `gateway*.discord.gg` TCP connections continue through the persistent selected proxy.
- A TCP CONNECT host is media control when its normalized hostname starts with `c-` and ends with `.discord.media`.
- Media-control connections use the same persistent proxy manager as the Gateway, preserving the destination port requested by Discord.
- Exact regional names and ports are never hardcoded.
- Other `discord.media` and subdomains remain direct.
- The relay remains TCP-only; UDP is not intercepted or proxied.
- Media-control connections remain proxied for their lifetime, and future matching connections also use the proxy so livestream actions can occur asynchronously.
- There is no direct fallback for a media-control connection if the selected proxy fails, matching the Gateway's location-safety behavior.

## Chromium Launch Policy

Remove the broad `--proxy-bypass-list=discord.media;*.discord.media` argument. With that bypass present, Chromium never sends `c-*.discord.media` TCP CONNECT requests to the local relay, so the relay cannot apply the selective policy.

All HTTPS/WSS CONNECT requests then reach the local relay. The relay itself keeps non-control media hosts direct, preventing broad media proxying. Chromium/WebRTC UDP remains outside this HTTP proxy path and continues normally.

## Proxy Lifecycle

Reuse the existing `GatewayProxyManager` for both Gateway and media-control connects. Its current connector already accepts arbitrary destination host and port. A successful matching connection resets the shared failure count; two consecutive establishment failures trigger the existing background proxy rotation. Rotation probes still validate candidates against `gateway.discord.gg:443`; target-specific media probing is deliberately excluded from this first experiment.

This limitation means a public proxy may support Gateway port 443 but reject a media-control port such as 8443. Verbose logs must distinguish Gateway and media-control routes so that such a failure is not mistaken for disproving the hostname hypothesis.

## Observability

With `--verbose`, log one sanitized line when establishing either route class:

- `Gateway via proxy: <host>:<port>`
- `Media control via proxy: <host>:<port>`

Do not log URLs, paths, query strings, headers, payloads, tokens, channel IDs, or account identifiers. Existing non-verbose behavior stays quiet.

## Testing

Automated tests cover:

- `c-*.discord.media` classification, case insensitivity, and rejection of lookalike suffix/prefix hosts;
- arbitrary destination ports remain intact;
- matching media control uses the persistent proxy connector before and after bootstrap-to-direct switching;
- ordinary `discord.media`, `latency.discord.media`, and unrelated hosts stay direct after switching;
- no direct fallback when media-control proxy connect fails;
- launcher no longer emits the broad media bypass argument;
- verbose route labels distinguish Gateway and media control;
- all existing Gateway persistence and relay behavior remains passing.

## Manual Acceptance

Build an experimental self-contained Windows executable without publishing it. Start Discord with the relay and VPN disabled, then test independently:

1. Discord starts successfully through the proxied Gateway.
2. Broadcaster joins voice and starts a livestream without VPN.
3. Viewer starts Discord and joins an existing livestream without VPN.
4. Broadcaster and viewer perform those actions at different times.
5. Voice and livestream media remain usable without high proxy bandwidth.
6. In `--verbose`, matching `c-*.discord.media` hosts are reported as media control via proxy.

If `c-*.discord.media` WebSocket connects through the proxy but livestream still fails, stop this hypothesis and return to evidence gathering for an unobserved UDP/WebRTC route. Do not broaden the proxy policy speculatively.

## Privacy

`NetLog/` contains private diagnostic data and must remain untracked. It is never included in commits, artifacts, releases, review packages, or public discussions. Only source, synthetic tests, and aggregate findings may enter the public repository.
