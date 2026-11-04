#!/usr/bin/env sh
# Optional per-user desktop integration. No root, system packages, or network access.
set -eu
source_dir=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
data_home=${XDG_DATA_HOME:-"$HOME/.local/share"}
case "$data_home" in /*) ;; *) echo 'XDG_DATA_HOME must be an absolute path.' >&2; exit 1;; esac
# The desktop-entry format cannot represent '=' in an executable path.
case "$data_home" in *'='*|*'
'*) echo 'The application path must not contain an equals sign or newline.' >&2; exit 1;; esac
target="$data_home/OpenSCCTLauncher"
desktop_dir="$data_home/applications"
icon_dir="$data_home/icons/hicolor/scalable/apps"
binary=OpenSCCT.Launcher-linux-x64
app_id=io.github.Lumbridge.OpenSCCT.Launcher
mkdir -p "$target" "$desktop_dir" "$icon_dir"
# Rename a fresh file into place; overwriting an executing Linux ELF fails with ETXTBSY.
install -m755 "$source_dir/$binary" "$target/$binary.installing"
mv -f -- "$target/$binary.installing" "$target/$binary"
install -m644 "$source_dir/openscct.svg" "$icon_dir/$app_id.svg"
if [ -d "$source_dir/licenses" ]; then cp -R "$source_dir/licenses" "$target/"; fi
# Desktop Entry string escaping happens before Exec quoting, hence doubled escapes.
escaped=$(printf '%s' "$target/$binary" | sed 's/\\/\\\\\\\\/g; s/["`$]/\\\\&/g; s/%/%%/g')
cat > "$desktop_dir/$app_id.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=OpenSCCT Launcher
Comment=Install, update and play OpenSCCT
Exec="$escaped" %u
Icon=$app_id
Terminal=false
Categories=Game;ActionGame;
MimeType=x-scheme-handler/openscct;
StartupNotify=true
EOF
chmod 644 "$desktop_dir/$app_id.desktop"
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database "$desktop_dir" || true; fi
if command -v xdg-mime >/dev/null 2>&1; then xdg-mime default "$app_id.desktop" x-scheme-handler/openscct || true; fi
printf 'Installed OpenSCCT Launcher. Open it from your application menu, or run:\n  "%s"\n' "$target/$binary"
