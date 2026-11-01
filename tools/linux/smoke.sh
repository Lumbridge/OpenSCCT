#!/usr/bin/env bash
set -euo pipefail
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
cd "$repo_root"
exe=${OPENSCCT_EXE:-$repo_root/local/linux/OpenSCCT.x86_64}
mkdir -p local/verification
for smoke in boot testbox; do
    "$exe" --headless -- --svm-smoke="$smoke" --svm-smoke-timeout=180 \
        > "local/verification/$smoke.log" 2>&1
    rg "SVM_SMOKE $smoke OK" "local/verification/$smoke.log"
done
if [[ ${OPENSCCT_NETWORK_TEST:-0} == 1 ]]; then
    mkdir -p local/setup-test/data local/setup-test/config
    XDG_DATA_HOME="$repo_root/local/setup-test/data" XDG_CONFIG_HOME="$repo_root/local/setup-test/config" \
        "$exe" --headless -- --svm-smoke=linux-setup --svm-smoke-timeout=600 \
        > local/verification/linux-setup.log 2>&1
    rg 'SVM_SMOKE linux-setup OK' local/verification/linux-setup.log
fi
