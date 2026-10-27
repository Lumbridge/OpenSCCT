<#
.SYNOPSIS
    Runs tools/ci/no_game_data.py with the first Python 3.10 or newer on PATH.

.DESCRIPTION
    Passes every argument through, for example:
        tools/ci/no_game_data.ps1
        tools/ci/no_game_data.ps1 --staged
        tools/ci/no_game_data.ps1 --all C:\some\release\folder
    Exits with the script's code: 0 clean, 1 problems found, 2 usage or git error.
#>

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'no_game_data.py'
$versionCheck = 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)'

$candidates = @(
    @{ Exe = 'python3'; Prefix = @() },
    @{ Exe = 'python'; Prefix = @() },
    @{ Exe = 'py'; Prefix = @('-3') }
)

foreach ($candidate in $candidates) {
    $command = Get-Command $candidate.Exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) { continue }
    $prefix = $candidate.Prefix
    # The Windows Store alias for python3 prints to stderr and fails; that must not stop the search.
    $ErrorActionPreference = 'SilentlyContinue'
    & $command.Source @prefix -c $versionCheck *> $null
    $usable = $LASTEXITCODE -eq 0
    $ErrorActionPreference = 'Stop'
    if (-not $usable) { continue }
    & $command.Source @prefix $script @args
    exit $LASTEXITCODE
}

Write-Error 'Python 3.10 or newer was not found on PATH.'
exit 2
