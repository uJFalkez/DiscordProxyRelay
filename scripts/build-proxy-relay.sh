#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts_parent="$repo_root/artifacts"
output_dir="$artifacts_parent/proxy-relay/win-x64"

dotnet test "$repo_root/tests/DiscordProxyRelay.Tests/DiscordProxyRelay.Tests.csproj" -c Release

rm -rf "$output_dir"
mkdir -p "$output_dir"
dotnet publish "$repo_root/src/DiscordProxyRelay/DiscordProxyRelay.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -o "$output_dir"

(cd "$output_dir" && sha256sum "DiscordProxyRelay.exe" > "SHA256SUMS.txt")

printf '%s\n' "Release artifact: artifacts/proxy-relay/win-x64/DiscordProxyRelay.exe"
printf '%s\n' "Checksums: artifacts/proxy-relay/win-x64/SHA256SUMS.txt"
