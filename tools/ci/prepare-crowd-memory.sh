#!/usr/bin/env bash
# The expanded Hub's 50 full client worlds exceed a standard hosted Linux runner's RAM.
set -euo pipefail
[[ "${RUNNER_ENVIRONMENT:-}" == github-hosted ]] || {
    echo 'Crowd memory provisioning requires a disposable GitHub-hosted runner.' >&2
    exit 1
}
swapfile="${RUNNER_TEMP:?}/openscct-crowd.swap"
required=$((16 * 1024 * 1024 * 1024))
available=$(df --output=avail -B1 "$RUNNER_TEMP" | tail -1)
if (( available < required + 2 * 1024 * 1024 * 1024 )); then
    # This disposable runner only builds .NET tests. Reclaim the unused Android SDK when necessary.
    sudo rm -rf /usr/local/lib/android
    available=$(df --output=avail -B1 "$RUNNER_TEMP" | tail -1)
fi
if (( available < required + 2 * 1024 * 1024 * 1024 )); then
    echo 'The full-world Hub soak needs 16 GiB of temporary swap and 2 GiB free disk.' >&2
    exit 1
fi
sudo fallocate -l "$required" "$swapfile"
sudo chmod 600 "$swapfile"
sudo mkswap "$swapfile"
sudo swapon "$swapfile"
free -h
