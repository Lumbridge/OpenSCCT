#!/usr/bin/env bash
# Install only into local/toolchain and the current user's Godot template directory.
set -euo pipefail
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
mkdir -p local/toolchain
cd local/toolchain
fetch() {
    if [[ ! -f "$2" ]]; then
        curl --fail --location --retry 3 "$1" -o "$2.partial"
        mv -- "$2.partial" "$2"
    fi
}
base=https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable
editor=Godot_v4.7.2-stable_mono_linux_x86_64.zip
templates=Godot_v4.7.2-stable_mono_export_templates.tpz
fetch "$base/$editor" "$editor"
fetch "$base/$templates" "$templates"
cat > godot.sha512 <<'SUMS'
1855960b27ee3ef5e66e5e228cced69d55637b24334a7411162687dcd077d8f9f645348cdb8eae984bec8135d49ed855a1e3a16476786b8bce60774fd8402d13  Godot_v4.7.2-stable_mono_linux_x86_64.zip
bb5c41d72370ed743660361f6228006f808ab04ca33abdc545d740b044f3fe057f32ae8cb7873a1bc86ddcd82ae683b9f6dfdfe4179852f2c0f1acde2ff6bd5a  Godot_v4.7.2-stable_mono_export_templates.tpz
SUMS
sha512sum -c godot.sha512
unzip -q -o "$editor"
python3 - "$templates" <<'PY'
import os,pathlib,sys,zipfile
out=pathlib.Path(os.environ.get('XDG_DATA_HOME',str(pathlib.Path.home()/'.local/share')))/'godot/export_templates/4.7.2.stable.mono'
with zipfile.ZipFile(sys.argv[1]) as z:
    for name in ['linux_release.x86_64','linux_debug.x86_64','version.txt']:
        p=out/name;p.parent.mkdir(parents=True,exist_ok=True)
        p.write_bytes(z.read('templates/'+name))
        if name.startswith('linux_'): p.chmod(0o755)
PY
if [[ ! -x "$repo_root/local/toolchain/dotnet/dotnet" ]]; then
    fetch https://dot.net/v1/dotnet-install.sh dotnet-install.sh
    bash dotnet-install.sh --version 10.0.302 --install-dir "$repo_root/local/toolchain/dotnet"
fi
printf '\nBuild with:\n  export PATH="%s/local/toolchain/dotnet:$PATH"\n  GODOT="%s/local/toolchain/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64" tools/linux/build.sh\n' "$repo_root" "$repo_root"
