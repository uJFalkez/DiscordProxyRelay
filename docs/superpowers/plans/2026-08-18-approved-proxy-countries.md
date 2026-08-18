# Approved Proxy Countries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Release DiscordProxyRelay 1.0.1 with a strict country allowlist and strict US/CA-first proxy probing.

**Architecture:** `ProxyCatalog` owns the country tiers, filters all unapproved entries, and returns at most six quality-ranked candidates for each of four stages. `ProxyProbe` consumes those tiers in the explicit order preferred SOCKS5, preferred HTTP, secondary SOCKS5, secondary HTTP, while retaining four-way concurrency inside one stage.

**Tech Stack:** C# 13, .NET 9, xUnit, GitHub CLI, Bash build script.

## Global Constraints

- Preferred countries are exactly `US` and `CA`.
- Secondary approved countries are exactly `GB`, `IE`, `DE`, `FR`, `NL`, `BE`, `LU`, `CH`, `AT`, `DK`, `NO`, `SE`, `FI`, `IS`, `AU`, `NZ`, `JP`, and `SG`.
- Never probe a country outside the allowlist.
- Probe stages in this exact order: preferred SOCKS5, preferred HTTP, secondary SOCKS5, secondary HTTP.
- Keep at most six candidates per stage and four concurrent probes within a stage.
- Rank each stage by uptime descending, average timeout ascending, then current timeout ascending.
- Preserve all RTC bypass, relay, Discord launch, TLS validation, privacy, and cancellation behavior.
- Version all product metadata as `1.0.1` / `1.0.1.0`.

---

### Task 1: Specify Catalog Policy

**Files:**
- Modify: `tests/DiscordProxyRelay.Tests/ProxyCatalogTests.cs`
- Test: `tests/DiscordProxyRelay.Tests/ProxyCatalogTests.cs`

**Interfaces:**
- Consumes: `ProxyCatalog.Parse(string json, int maximumCandidates = 12)`.
- Produces: failing tests for the exact allowlist, four-stage order, quality ranking, normalization, and six-per-stage cap.

- [ ] **Step 1: Replace broad non-BR expectations with the exact allowlist**

Add test data that accepts lowercase `us`, `CA`, `DE`, and `JP`, while rejecting `BR`, `KZ`, `CN`, `RU`, `ZZ`, missing values, and whitespace-only values. Assert returned country codes are uppercase.

- [ ] **Step 2: Specify four-stage catalog order and ranking**

Use candidates from `US`, `CA`, `DE`, and `FR` across both protocols. Assert the returned ports are grouped as preferred SOCKS5, preferred HTTP, secondary SOCKS5, secondary HTTP, with quality metrics deciding order only inside each group.

- [ ] **Step 3: Specify the six-per-stage cap**

Generate seven entries for each of the four stages and assert exactly 24 results, six from each stage, with the seventh candidate absent.

- [ ] **Step 4: Run catalog tests and verify RED**

Run:

```bash
dotnet test tests/DiscordProxyRelay.Tests/DiscordProxyRelay.Tests.csproj -c Release --filter FullyQualifiedName~ProxyCatalogTests
```

Expected: the new allowlist and stage-cap assertions fail because the current implementation accepts any known non-BR country and keeps 12 per protocol.

### Task 2: Implement Catalog Policy

**Files:**
- Modify: `src/DiscordProxyRelay/ProxyCatalog.cs`
- Test: `tests/DiscordProxyRelay.Tests/ProxyCatalogTests.cs`

**Interfaces:**
- Produces: `ProxyCatalog.IsPreferredCountry(string)`, `ProxyCatalog.IsApprovedCountry(string)`, and `ProxyCatalog.MaximumCandidatesPerStage` for use by `ProxyProbe`.

- [ ] **Step 1: Replace runtime-derived countries with fixed tiers**

Remove `System.Globalization`, `KnownCountries`, and `BuildKnownCountries`. Define case-insensitive preferred and secondary sets containing exactly the codes in Global Constraints, plus:

```csharp
internal const int MaximumCandidatesPerStage = 6;

internal static bool IsPreferredCountry(string countryCode) =>
    PreferredCountries.Contains(countryCode);

internal static bool IsApprovedCountry(string countryCode) =>
    IsPreferredCountry(countryCode) || SecondaryCountries.Contains(countryCode);
```

- [ ] **Step 2: Filter and rank four stages**

Normalize with `ToUpperInvariant()`, reject `!IsApprovedCountry(countryCode)`, and return:

```csharp
return Rank(ProxyKind.Socks5, preferred: true)
    .Concat(Rank(ProxyKind.Http, preferred: true))
    .Concat(Rank(ProxyKind.Socks5, preferred: false))
    .Concat(Rank(ProxyKind.Http, preferred: false))
    .Select(proxy => proxy.Endpoint)
    .ToArray();
```

The local `Rank` function must filter by protocol and preferred tier, apply the existing three quality sort keys, and take `Math.Min(maximumCandidates, MaximumCandidatesPerStage)`.

- [ ] **Step 3: Run catalog tests and verify GREEN**

Run the filtered test command from Task 1. Expected: all `ProxyCatalogTests` pass.

- [ ] **Step 4: Commit the catalog policy**

Stage only `ProxyCatalog.cs` and `ProxyCatalogTests.cs`, then commit with the personal `uJFalkez` noreply identity and message `feat: restrict proxy countries`.

### Task 3: Specify And Implement Strict Probe Stages

**Files:**
- Modify: `tests/DiscordProxyRelay.Tests/ProxyProbeTests.cs`
- Modify: `src/DiscordProxyRelay/ProxyProbe.cs`

**Interfaces:**
- Consumes: `ProxyCatalog.IsPreferredCountry`, `ProxyCatalog.IsApprovedCountry`, and `ProxyCatalog.MaximumCandidatesPerStage`.
- Produces: strict sequential stage probing through `ProxyProbe.FindUsableAsync`.

- [ ] **Step 1: Write failing ordering tests**

Add one test proving a successful preferred HTTP candidate is selected without attempting a secondary SOCKS5 candidate. Add another test where preferred SOCKS5, preferred HTTP, and secondary SOCKS5 fail before secondary HTTP succeeds. Record attempted stages in the connector callback and assert exact stage boundaries.

- [ ] **Step 2: Update cap coverage**

Change existing cap assertions from 12 candidates per protocol to six candidates per stage. Add an unapproved `KZ` candidate and assert it is never attempted.

- [ ] **Step 3: Run probe tests and verify RED**

Run:

```bash
dotnet test tests/DiscordProxyRelay.Tests/DiscordProxyRelay.Tests.csproj -c Release --filter FullyQualifiedName~ProxyProbeTests
```

Expected: strict country ordering and unapproved-country rejection fail against the current protocol-only grouping.

- [ ] **Step 4: Implement four sequential groups**

Build four groups in `FindUsableAsync` using the exact stage order. Each group filters by country tier and protocol and applies `.Take(ProxyCatalog.MaximumCandidatesPerStage)`. Iterate groups sequentially, returning the first non-null result from the existing concurrent `ProbeGroupAsync`; return `null` after all groups fail.

- [ ] **Step 5: Run probe and catalog tests and verify GREEN**

Run:

```bash
dotnet test tests/DiscordProxyRelay.Tests/DiscordProxyRelay.Tests.csproj -c Release --filter 'FullyQualifiedName~ProxyProbeTests|FullyQualifiedName~ProxyCatalogTests'
```

Expected: all filtered tests pass.

- [ ] **Step 6: Commit strict probing**

Stage only `ProxyProbe.cs` and `ProxyProbeTests.cs`, then commit with the personal identity and message `feat: prioritize US and CA proxies`.

### Task 4: Update Failure Copy, Documentation, And Version

**Files:**
- Modify: `tests/DiscordProxyRelay.Tests/LauncherAppTests.cs`
- Modify: `src/DiscordProxyRelay/LauncherApp.cs`
- Modify: `src/DiscordProxyRelay/DiscordProxyRelay.csproj`
- Modify: `README.md`

**Interfaces:**
- Produces: public `1.0.1` metadata and documentation matching runtime behavior.

- [ ] **Step 1: Update the launcher failure assertion first**

Expect this exact sentence:

```text
Nenhum proxy utilizável dos países aprovados foi encontrado. O Discord não será iniciado.
```

Run `LauncherAppTests` and verify the updated assertion fails against the old copy.

- [ ] **Step 2: Implement the new failure copy**

Replace only the corresponding output line in `LauncherApp.cs`, then rerun `LauncherAppTests` and expect a pass.

- [ ] **Step 3: Set version metadata**

Set `<Version>` and `<InformationalVersion>` to `1.0.1`; set `<AssemblyVersion>` and `<FileVersion>` to `1.0.1.0`.

- [ ] **Step 4: Update README behavior**

Replace the broad outside-Brazil description with the exact preferred and secondary allowlists. Document strict US/CA-first stage order, no geographic fallback, the 24-attempt cap, and the fact that geographic filtering does not make public proxies trustworthy.

- [ ] **Step 5: Run the complete test suite**

Run:

```bash
dotnet test tests/DiscordProxyRelay.Tests/DiscordProxyRelay.Tests.csproj -c Release
```

Expected: all tests pass with zero failures.

- [ ] **Step 6: Commit docs and metadata**

Stage only the four files listed in this task and commit with the personal identity and message `docs: prepare v1.0.1`.

### Task 5: Build, Review, And Publish 1.0.1

**Files:**
- Generated, ignored: `artifacts/proxy-relay/win-x64/DiscordProxyRelay.exe`
- Generated, ignored: `artifacts/proxy-relay/win-x64/SHA256SUMS.txt`

**Interfaces:**
- Consumes: the complete `main` source tree.
- Produces: public tag and GitHub Release `v1.0.1` with executable, checksum, and .NET notices.

- [ ] **Step 1: Run the release build**

Run `./scripts/build-proxy-relay.sh`. Expected: complete tests pass, publish succeeds, and both release artifacts are regenerated.

- [ ] **Step 2: Verify runtime and checksum**

Confirm the publish deps reference `runtimepack.Microsoft.NETCore.App.Runtime.win-x64/9.0.19`; run `sha256sum -c SHA256SUMS.txt` from the artifact directory and expect `DiscordProxyRelay.exe: OK`.

- [ ] **Step 3: Request final code review**

Review all commits since the prior public `main`, fixing every Critical or Important finding before publication.

- [ ] **Step 4: Push source and create release**

Push `main`, create tag `v1.0.1` at the reviewed HEAD, and create a non-draft, non-prerelease GitHub Release containing `DiscordProxyRelay.exe`, `SHA256SUMS.txt`, `DOTNET-LICENSE.txt`, and `DOTNET-THIRD-PARTY-NOTICES.txt`.

- [ ] **Step 5: Verify the public release**

Confirm anonymous repository access, tag/branch SHAs, contributor attribution to `uJFalkez`, all four asset names and sizes, and a freshly downloaded executable matching `SHA256SUMS.txt`.
