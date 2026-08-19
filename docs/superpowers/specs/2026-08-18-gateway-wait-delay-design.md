# Gateway Wait Delay Design

## Goal

Allow users with slower Discord startup to override the ten-second delay between observing the proxied gateway and performing the existing hard switch to direct connections.

## Command Line

The launcher accepts:

```text
DiscordProxyRelay.exe [--verbose] [--gateway-wait-delay <seconds>]
```

`--gateway-wait-delay` accepts an integer from 1 through 600, inclusive. The default remains 10 seconds. `--verbose` and the delay option may appear in either order, but neither option may be repeated. Missing values, non-integers, out-of-range values, duplicate options, and unknown options are rejected with the usage message and exit code 1.

## Runtime Behavior

The configured value replaces only the ten-second delay after `GatewayObserved` completes. The 60-second gateway-observation timeout and five-second post-switch delay remain unchanged.

The launcher reports the effective delay:

```text
Gateway observado pelo proxy. Aguardando 60 segundos antes da troca definitiva...
```

The relay, hard switch, country policy, proxy probing, and `discord.media` bypass do not change.

## Structure

`Program` owns argument parsing and produces a small immutable options value containing `Verbose` and `GatewayWaitDelay`. `LauncherDependencies` receives the validated delay, allowing `LauncherApp` and its tests to use the effective value without reading command-line state.

## Documentation And Version

The README documents the new option as a workaround for slow startup and states that it does not guarantee regional features after the hard switch. Product metadata advances to `1.0.2` / `1.0.2.0`.

## Verification

Tests cover defaults, both option orders, boundaries 1 and 600, missing/invalid/out-of-range/duplicate/unknown arguments, runtime use of the override, and unchanged timeout/post-switch delays. The complete suite and release build must pass before publishing `v1.0.2`.
