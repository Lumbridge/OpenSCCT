#!/usr/bin/env bash
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
: "${GODOT:?Set GODOT to the pinned Godot .NET Linux editor executable}"
godot=$(realpath "$GODOT")
build=${SCCT_AUDIO_BUILD_DIR:-"$repo/local/audio-native/linux-x86_64"}
mkdir -p "$build/api-dump"
(cd "$build/api-dump" && "$godot" --headless --dump-extension-api)
if ! cmp -s "$build/api-dump/extension_api.json" "$build/extension_api.json"; then
    cp "$build/api-dump/extension_api.json" "$build/extension_api.json"
fi
dependencies=()
if [[ -n "${SCCT_AUDIO_DEPS_DIR:-}" ]]; then
    dependencies=(-DFETCHCONTENT_SOURCE_DIR_GODOTCPP="$SCCT_AUDIO_DEPS_DIR/godot-cpp"
        -DFETCHCONTENT_SOURCE_DIR_OPENAL="$SCCT_AUDIO_DEPS_DIR/openal-soft"
        -DFETCHCONTENT_SOURCE_DIR_REAL="$SCCT_AUDIO_DEPS_DIR/real")
fi
cmake -S "$repo/src/OpenScct.Audio.Native" -B "$build" -G Ninja -DCMAKE_BUILD_TYPE=Release \
    -DGODOTCPP_CUSTOM_API_FILE="$build/extension_api.json" \
    -DSCCT_AUDIO_REVERB_BACKEND="${SCCT_AUDIO_REVERB_BACKEND:-HRVB}" "${dependencies[@]}"
cmake --build "$build" --parallel "${SCCT_AUDIO_BUILD_JOBS:-4}"
cmake --install "$build" --prefix "$repo/game/addons/scct_audio"
