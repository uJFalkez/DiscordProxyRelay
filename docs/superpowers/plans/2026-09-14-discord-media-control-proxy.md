# Discord Media Control Proxy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route dynamic `c-*.discord.media` TCP control connections through the same persistent proxy used by Discord Gateway while leaving other media and UDP direct.

**Architecture:** Extend `ConnectAuthority` with one strict media-control classifier. `RelayServer` treats Gateway and media control as persistent location-sensitive routes when a `GatewayProxyManager` exists, while retaining temporary-mode bootstrap behavior. Remove Chromium's broad media bypass so matching CONNECT requests reach the relay.

**Tech Stack:** C# 13, .NET 9.0.19, xUnit 2.9.2, existing TCP relay and proxy abstractions.

## Global Constraints

- Match hostnames case-insensitively only when they start with `c-` and end with `.discord.media`.
- Never hardcode regional hostnames or destination ports.
- Preserve the CONNECT destination port exactly.
- Gateway and media control share the existing persistent proxy manager and failure/rotation state.
- Ordinary `discord.media`, `latency.discord.media`, and other media subdomains remain direct.
- UDP remains outside the relay and direct.
- Persistent media control has no direct fallback on proxy failure.
- `--temporary-gateway` keeps temporary semantics: media control uses the bootstrap proxy before switching and direct routing afterward.
- Verbose output contains only route class plus host and port; non-verbose remains quiet.
- Do not stage or commit `NetLog/`, logger code, diagnostic files, or build artifacts.
- Build an experimental artifact only; do not push, tag, or publish.

---

### Task 1: Classify And Route Media Control

**Files:**
- Modify: `src/DiscordProxyRelay/ProxyEndpoint.cs`
- Modify: `src/DiscordProxyRelay/RelayServer.cs`
- Modify: `src/DiscordProxyRelay/DiscordLauncher.cs`
- Modify: `tests/DiscordProxyRelay.Tests/ConnectAuthorityTests.cs`
- Modify: `tests/DiscordProxyRelay.Tests/RelayServerTests.cs`
- Modify: `tests/DiscordProxyRelay.Tests/DiscordLauncherTests.cs`

**Interfaces:**
- Produces: `ConnectAuthority.IsDiscordMediaControl`.
- Consumes: existing `IGatewayProxyConnector.ConnectAsync(host, port, token)` and verbose callback.

- [ ] **Step 1: Write failing classifier and launcher tests**

Add classifier cases:

```csharp
[InlineData("c-gru08-abc.discord.media", true)]
[InlineData("C-EWR14-ABC.DISCORD.MEDIA", true)]
[InlineData("c-x.discord.media", true)]
[InlineData("discord.media", false)]
[InlineData("latency.discord.media", false)]
[InlineData("xc-test.discord.media", false)]
[InlineData("c-.discord.media.example.com", false)]
[InlineData("c-discord.media", false)]
```

Change launcher expectation to exactly one argument:

```csharp
Assert.Equal(["--proxy-server=http://127.0.0.1:32123"], info.ArgumentList);
```

- [ ] **Step 2: Write failing relay tests**

Test that, with persistent connector enabled:

- `c-gru.discord.media:8443` uses `IGatewayProxyConnector` before switching;
- the same host still uses it after switching;
- the connector receives host and port 8443 unchanged;
- the open media-control tunnel survives `SwitchToDirectAsync`;
- `discord.media`, `latency.discord.media`, and `video.discord.media` use direct connector;
- a failing persistent media-control connect returns HTTP 502 and does not invoke direct connector;
- verbose callbacks produce `Media control via proxy: c-gru.discord.media:8443` and `Gateway via proxy: gateway.discord.gg:443`;
- temporary mode uses bootstrap before switching and direct after switching.

- [ ] **Step 3: Run focused tests and confirm RED**

Run:

```bash
dotnet test tests/DiscordProxyRelay.Tests/DiscordProxyRelay.Tests.csproj -c Release --filter "ConnectAuthorityTests|DiscordLauncherTests|RelayServerTests"
```

Expected: FAIL because the classifier and route do not exist and the bypass is still emitted.

- [ ] **Step 4: Implement the minimum route change**

Add:

```csharp
public bool IsDiscordMediaControl =>
    Host.StartsWith("c-", StringComparison.OrdinalIgnoreCase) &&
    Host.EndsWith(".discord.media", StringComparison.OrdinalIgnoreCase);
```

In `RelayServer`, compute ordinary direct RTC only for media that is not media control. Compute one persistent location route for either Gateway or media control when `_gatewayProxyConnector` exists. Use that connector unchanged, preserve the existing bootstrap token behavior, and emit the two exact verbose labels with `authority.Value`. Keep `GatewayObserved` tied only to Gateway.

Remove only this line from `DiscordLauncher`:

```csharp
startInfo.ArgumentList.Add("--proxy-bypass-list=discord.media;*.discord.media");
```

- [ ] **Step 5: Run focused then full tests**

Run the focused command from Step 3.

Expected: PASS.

Run:

```bash
dotnet test tests/DiscordProxyRelay.Tests/DiscordProxyRelay.Tests.csproj -c Release
```

Expected: all relay tests PASS.

- [ ] **Step 6: Build experimental artifact and verify privacy**

Run:

```bash
./scripts/build-proxy-relay.sh
```

Expected: tests and publish PASS; executable and checksum exist under ignored `artifacts/proxy-relay/win-x64`.

Run `git status --short --ignored` and inspect staged/tracked paths. No `NetLog/`, logger files, diagnostic JSON, executable, or checksum may be tracked.

- [ ] **Step 7: Commit source and synthetic tests only**

```bash
git add src/DiscordProxyRelay/ProxyEndpoint.cs src/DiscordProxyRelay/RelayServer.cs src/DiscordProxyRelay/DiscordLauncher.cs tests/DiscordProxyRelay.Tests/ConnectAuthorityTests.cs tests/DiscordProxyRelay.Tests/RelayServerTests.cs tests/DiscordProxyRelay.Tests/DiscordLauncherTests.cs
git commit -m "feat: proxy Discord media control connections"
```
