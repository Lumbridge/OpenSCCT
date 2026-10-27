#!/usr/bin/env bash
# Proves the banned-API analyzer works: builds a throwaway sim library that calls MathF.Sin and expects the build
# to fail with RS0030. The probe picks up the repository's Directory.Build.props, Directory.Packages.props and
# build/Sim.props like a real sim library. Exits 0 only when the build fails with RS0030 for MathF.Sin.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/svm-banned-api-XXXXXXXX")"
keep_temp="${KEEP_TEMP:-0}"

cleanup() {
    if [[ "$keep_temp" != "1" ]]; then
        rm -rf "$work"
    fi
}
trap cleanup EXIT

# MSBuild wants native paths; on Git Bash for Windows convert /c/... to C:/...
native_root="$repo_root"
if command -v cygpath >/dev/null 2>&1; then
    native_root="$(cygpath -m "$repo_root")"
fi

cat > "$work/Directory.Build.props" <<EOF
<Project>
  <Import Project="$native_root/Directory.Build.props" />
</Project>
EOF

cat > "$work/Directory.Packages.props" <<EOF
<Project>
  <Import Project="$native_root/Directory.Packages.props" />
</Project>
EOF

cp "$repo_root/global.json" "$work/"

cat > "$work/BannedApiProbe.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$(RepoRoot)build/Sim.props" />
</Project>
EOF

cat > "$work/Probe.cs" <<'EOF'
namespace BannedApiProbe;

public static class Probe
{
    public static float Run(float x) => System.MathF.Sin(x);
}
EOF

echo "Building probe in $work"
set +e
output="$(cd "$work" && dotnet build BannedApiProbe.csproj -nologo 2>&1)"
exit_code=$?
set -e

diagnostics="$(printf '%s\n' "$output" | grep 'RS0030' | sort -u || true)"
if [[ -n "$diagnostics" ]]; then
    printf '%s\n' "$diagnostics"
fi

if [[ $exit_code -ne 0 ]] && printf '%s\n' "$diagnostics" | grep -q 'MathF\.Sin'; then
    echo "OK: the build failed with RS0030 for MathF.Sin, so banned APIs are enforced."
    exit 0
fi

if [[ $exit_code -eq 0 ]]; then
    echo "FAIL: the probe built successfully; the banned-API analyzer is not enforced."
else
    echo "FAIL: the build failed (exit $exit_code) but not with RS0030 for MathF.Sin. Build output:"
    printf '%s\n' "$output"
fi
exit 1
