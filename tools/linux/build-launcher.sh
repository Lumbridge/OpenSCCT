#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
output="$repo_root/local/publish/launcher-linux-x64"
dotnet publish src/OpenScct.Launcher.Linux -c Release -r linux-x64 --self-contained true \
    -p:UseSharedCompilation=false -o "$output"
chmod +x "$output/OpenSCCT.Launcher-linux-x64"
mkdir -p local/dist
install -m755 "$output/OpenSCCT.Launcher-linux-x64" local/dist/OpenSCCT.Launcher-linux-x64
python3 tools/linux/package-launcher.py local/dist/OpenSCCT.Launcher-linux-x64 local/dist
