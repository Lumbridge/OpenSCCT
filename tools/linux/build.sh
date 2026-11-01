#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
: "${GODOT:?Set GODOT to the Godot 4.7.2 .NET Linux editor executable}"
GODOT=$(realpath "$GODOT")
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_PROCESSOR_COUNT=${DOTNET_PROCESSOR_COUNT:-2}
export MSBUILDDISABLENODEREUSE=1
mkdir -p local/linux
# The editor loads Debug assemblies during import. The exporter publishes ExportRelease itself.
# Compile sequentially, avoiding persistent compiler servers on shared build machines.
nice -n 10 dotnet build game/OpenScctGame.csproj -c Debug -m:1 -p:UseSharedCompilation=false
nice -n 10 "$GODOT" --headless --path game --editor --import
nice -n 10 "$GODOT" --headless --path game --export-release Linux "$repo_root/local/linux/OpenSCCT.x86_64"
mkdir -p local/linux/licenses
cp -a packaging/licenses/. local/linux/licenses/
mkdir -p local/linux/maps
for map in hub hubmap testbox; do cp -a "fixtures/$map" local/linux/maps/; done
python3 - <<'PY'
import datetime,json,subprocess
from pathlib import Path
Path('local/linux/openscct-build.json').write_text(json.dumps({
    'platform':'linux-x64',
    'commit':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
    'dirty':bool(subprocess.check_output(['git','status','--porcelain'],text=True).strip()),
    'built':datetime.datetime.now(datetime.timezone.utc).isoformat(),
},indent=2)+'\n')
PY
"$repo_root/tools/linux/package-flatpak.sh" local/linux
