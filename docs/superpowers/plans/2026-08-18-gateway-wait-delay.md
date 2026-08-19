# Gateway Wait Delay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Release DiscordProxyRelay 1.0.2 with a validated `--gateway-wait-delay <seconds>` override while preserving the ten-second default.

**Architecture:** `Program.TryParseArguments` validates CLI input into immutable `LauncherOptions`. `LauncherDependencies` carries the resulting `TimeSpan` to `LauncherApp`, which uses it only for the existing post-gateway delay.

**Tech Stack:** C# 13, .NET 9, xUnit, Bash release script.

## Global Constraints

- Default gateway wait remains exactly 10 seconds.
- Accepted overrides are integer seconds from 1 through 600 inclusive.
- `--verbose` and `--gateway-wait-delay` work in either order and cannot be repeated.
- Invalid arguments print `Uso: DiscordProxyRelay.exe [--verbose] [--gateway-wait-delay <seconds>]` and exit 1.
- Gateway observation timeout remains 60 seconds; post-switch delay remains 5 seconds.
- Relay routing, hard switch, country policy, probing, and media bypass remain unchanged.
- Product version is `1.0.2`; assembly/file version is `1.0.2.0`.

---

### Task 1: Parse Gateway Delay Options

**Files:**
- Create: `tests/DiscordProxyRelay.Tests/ProgramTests.cs`
- Modify: `src/DiscordProxyRelay/Program.cs`

**Interfaces:**
- Produces: `LauncherOptions(bool Verbose, TimeSpan GatewayWaitDelay)` and `Program.TryParseArguments(string[] args, out LauncherOptions options)`.

- [ ] Write tests for empty args, verbose only, delay only, both orders, values 1 and 600, and rejection of missing, zero, 601, non-integer, duplicate, and unknown arguments.
- [ ] Run `dotnet test tests/DiscordProxyRelay.Tests/DiscordProxyRelay.Tests.csproj -c Release --filter FullyQualifiedName~ProgramTests` and observe RED because the parser does not exist.
- [ ] Implement one-pass parsing with invariant integer parsing, duplicate detection, a ten-second default, and the exact usage string.
- [ ] Re-run `ProgramTests` and expect all tests to pass.

### Task 2: Apply The Configured Delay

**Files:**
- Modify: `tests/DiscordProxyRelay.Tests/LauncherAppTests.cs`
- Modify: `src/DiscordProxyRelay/LauncherApp.cs`
- Modify: `src/DiscordProxyRelay/Program.cs`

**Interfaces:**
- Consumes: `LauncherOptions.GatewayWaitDelay`.
- Produces: `LauncherDependencies.GatewayWaitDelay` used by `LauncherApp`.

- [ ] Update the gateway-success test to inject 60 seconds and require `delay:60`; add assertions that the 60-second observation timeout and 5-second post-switch delay remain unchanged.
- [ ] Run filtered `LauncherAppTests` and observe RED against the fixed ten-second implementation.
- [ ] Add `GatewayWaitDelay` to `LauncherDependencies`, pass it from `CreateDefault`, use it in the status message and `dependencies.Delay`, and wire parsed options from `Program.Main`.
- [ ] Run `ProgramTests` and `LauncherAppTests`; expect all to pass.

### Task 3: Document And Version 1.0.2

**Files:**
- Modify: `README.md`
- Modify: `src/DiscordProxyRelay/DiscordProxyRelay.csproj`

- [ ] Document both command examples, valid range, ten-second default, slow-PC use case, and the limitation that delaying the hard switch is not a guarantee.
- [ ] Set version fields to `1.0.2` and `1.0.2.0`.
- [ ] Run the complete test suite and `./scripts/build-proxy-relay.sh`.
- [ ] Verify runtime 9.0.19 and the generated SHA-256 manifest.
- [ ] Request final review before tagging or publishing `v1.0.2`.
