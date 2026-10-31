#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
mkdir -p local/verification
for smoke in boot testbox; do
    dbus-run-session -- flatpak run --user io.github.Lumbridge.OpenSCCT --headless -- \
        --svm-smoke="$smoke" --svm-smoke-timeout=180 \
        > "local/verification/flatpak-$smoke.log" 2>&1
    rg "SVM_SMOKE $smoke OK" "local/verification/flatpak-$smoke.log"
done
