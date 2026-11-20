#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
exe=${OPENSCCT_EXE:-$repo_root/local/linux/OpenSCCT.x86_64}
mkdir -p local/verification
for smoke in boot testbox help-feedback; do
    "$exe" --headless -- --svm-smoke="$smoke" --svm-smoke-timeout=180 \
        > "local/verification/$smoke.log" 2>&1
    rg "SVM_SMOKE $smoke OK" "local/verification/$smoke.log"
done
setup_dir=$(mktemp -d)
trap 'rm -rf -- "$setup_dir"' EXIT
XDG_DATA_HOME="$setup_dir/data" XDG_CONFIG_HOME="$setup_dir/config" OPENSCCT_GODOT_CONTENT_DIR="$setup_dir/content" \
    "$exe" --headless -- --svm-content="$setup_dir/content" --svm-smoke=linux-setup --svm-smoke-timeout=180 \
    > local/verification/linux-setup.log 2>&1
rg 'SVM_SMOKE linux-setup OK' local/verification/linux-setup.log
