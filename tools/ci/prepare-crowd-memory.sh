#!/usr/bin/env bash
# The expanded Hub's 50 full client worlds exceed a standard hosted Linux runner's RAM.
set -euo pipefail
swapfile="${RUNNER_TEMP:?}/openscct-crowd.swap"
required=$((12 * 1024 * 1024 * 1024))
available=$(df --output=avail -B1 "$RUNNER_TEMP" | tail -1)
if (( available < required + 512 * 1024 * 1024 )); then
    echo 'The full-world Hub soak needs 12 GiB of temporary swap and 512 MiB free disk.' >&2
    exit 1
fi
sudo fallocate -l "$required" "$swapfile"
sudo chmod 600 "$swapfile"
sudo mkswap "$swapfile"
sudo swapon "$swapfile"
free -h
