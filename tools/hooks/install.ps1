<#
.SYNOPSIS
    Points git at the hooks in tools/hooks for this clone.

.DESCRIPTION
    Sets core.hooksPath to tools/hooks, so the pre-commit hook runs tools/ci/no_game_data.py --staged
    before every commit. Run it once per clone. -Uninstall removes the setting again.

.PARAMETER Uninstall
    Unset core.hooksPath instead of setting it.
#>
[CmdletBinding()]
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ($Uninstall) {
    git -C $repoRoot config --unset core.hooksPath
    # Exit code 5 means the setting was not there, which is fine.
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 5) { throw "git config failed with exit code $LASTEXITCODE." }
    Write-Host "core.hooksPath removed for $repoRoot"
    exit 0
}

git -C $repoRoot config core.hooksPath tools/hooks
if ($LASTEXITCODE -ne 0) { throw "git config failed with exit code $LASTEXITCODE." }

# Git only runs hooks that are executable on Linux and macOS.
if ($PSVersionTable.PSEdition -eq 'Core' -and -not $IsWindows) {
    chmod +x (Join-Path $PSScriptRoot 'pre-commit')
}

Write-Host "core.hooksPath set to tools/hooks for $repoRoot"
