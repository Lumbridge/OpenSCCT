#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
export_dir=${1:-local/linux}
export_dir=$(realpath "$export_dir")
output_dir="$repo_root/local/dist"
mkdir -p "$repo_root/local"
staging=$(mktemp -d "$repo_root/local/flatpak-build.XXXXXX")
trap 'rm -rf -- "$staging"' EXIT
app_id=io.github.Lumbridge.OpenSCCT
[[ -x "$export_dir/OpenSCCT.x86_64" && -f "$export_dir/OpenSCCT.pck" ]] || {
    echo 'Export the Linux preset first (tools/linux/build.sh).' >&2; exit 1;
}
mkdir -p "$output_dir" "$staging/files/bin" "$staging/files/openscct" \
    "$staging/files/share/applications" "$staging/files/share/metainfo" \
    "$staging/files/share/icons/hicolor/scalable/apps"
cp -a "$export_dir/." "$staging/files/openscct/"
mkdir -p "$staging/files/share/licenses/$app_id"
cp -a packaging/licenses/. "$staging/files/share/licenses/$app_id/"
install -m755 packaging/flatpak/openscct "$staging/files/bin/openscct"
install -m644 "packaging/flatpak/$app_id.desktop" "$staging/files/share/applications/"
install -m644 "packaging/flatpak/$app_id.metainfo.xml" "$staging/files/share/metainfo/"
install -m644 game/branding/icon.svg "$staging/files/share/icons/hicolor/scalable/apps/$app_id.svg"
cat > "$staging/metadata" <<'META'
[Application]
name=io.github.Lumbridge.OpenSCCT
runtime=org.freedesktop.Platform/x86_64/25.08
sdk=org.freedesktop.Sdk/x86_64/25.08
command=openscct

[Context]
shared=network;ipc;
sockets=x11;wayland;pulseaudio;
devices=all;

[Environment]
DOTNET_EnableDiagnostics=0
META
# build-finish and build-export do not execute the game or require a nested sandbox.
flatpak build-finish "$staging"
# Some container hosts disallow nested namespaces. This only disables the icon validator's sandbox.
flatpak build-export --disable-sandbox "$repo_root/local/flatpak-repo" "$staging" stable
flatpak build-bundle --runtime-repo=https://flathub.org/repo/flathub.flatpakrepo \
    "$repo_root/local/flatpak-repo" "$output_dir/OpenSCCT-linux-x86_64.flatpak" "$app_id" stable
(cd "$output_dir" && sha256sum OpenSCCT-linux-x86_64.flatpak > SHA256SUMS)
echo "Bundle: $output_dir/OpenSCCT-linux-x86_64.flatpak"
