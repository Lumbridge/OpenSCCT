#!/usr/bin/env bash
set -euo pipefail
archive=$(realpath "${1:-local/dist/OpenSCCT.Launcher-linux-x64.tar.gz}")
temp=$(mktemp -d)
trap 'rm -rf -- "$temp"' EXIT
# Exercise paths with spaces and installer placement separately from game data.
player_dir="$temp/player home"
export XDG_DATA_HOME="$player_dir/data"
export XDG_CONFIG_HOME="$player_dir/config"
export XDG_CACHE_HOME="$player_dir/cache"
export XDG_RUNTIME_DIR="$temp/runtime"
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$temp/bundle"
mkdir -p "$player_dir" "$XDG_RUNTIME_DIR"
chmod 700 "$XDG_RUNTIME_DIR"
tar -xzf "$archive" -C "$temp"
launcher="$temp/OpenSCCT-Launcher/OpenSCCT.Launcher-linux-x64"
test -x "$launcher"
"$launcher" --version
timeout 90 "$launcher" --smoke
bash "$temp/OpenSCCT-Launcher/install.sh"
desktop="$XDG_DATA_HOME/applications/io.github.Lumbridge.OpenSCCT.Launcher.desktop"
desktop-file-validate "$desktop"
installed="$XDG_DATA_HOME/OpenSCCTLauncher/OpenSCCT.Launcher-linux-x64"
test -x "$installed"
cmp "$launcher" "$installed"
timeout 90 xvfb-run -a "$installed" --smoke-ui
echo 'Native launcher, install integration and desktop UI smokes passed.'
