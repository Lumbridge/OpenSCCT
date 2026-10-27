<#
.SYNOPSIS
    Creates re/.venv-ghidra (Python 3.10) with the PyGhidra and JPype wheels that ship with Ghidra.

.DESCRIPTION
    Installs offline from Ghidra\Features\PyGhidra\pypkg\dist, so the versions always match the Ghidra
    install. Safe to re-run: an existing venv is reused and the packages are reinstalled only when missing.

.PARAMETER GhidraDir
    Ghidra install root. Defaults to $env:GHIDRA_INSTALL_DIR, else $env:USERPROFILE\Desktop\ghidra_12.2_PUBLIC.
#>
[CmdletBinding()]
param(
    [string]$GhidraDir = $(if ($env:GHIDRA_INSTALL_DIR) { $env:GHIDRA_INSTALL_DIR } else { (Join-Path $env:USERPROFILE 'Desktop\ghidra_12.2_PUBLIC') })
)

$ErrorActionPreference = 'Stop'
$reDir = Split-Path -Parent $PSScriptRoot
$venv = Join-Path $reDir '.venv-ghidra'
$venvPython = Join-Path $venv 'Scripts\python.exe'
$dist = Join-Path $GhidraDir 'Ghidra\Features\PyGhidra\pypkg\dist'

if (-not (Test-Path $dist)) { throw "PyGhidra wheels not found at $dist" }

if (-not (Test-Path $venvPython)) {
    Write-Host "Creating $venv with Python 3.10"
    & py -3.10 -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw 'py -3.10 -m venv failed' }
}

& $venvPython -c 'import pyghidra, jpype' 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installing pyghidra from $dist (offline)"
    & $venvPython -m pip install --disable-pip-version-check --no-index --find-links $dist pyghidra
    if ($LASTEXITCODE -ne 0) { throw 'pip install pyghidra failed' }
}

$versions = & $venvPython -c 'import importlib.metadata as m; print("pyghidra", m.version("pyghidra"), "jpype1", m.version("jpype1"))'
Write-Host "Ready: $versions"
Write-Host ''
Write-Host 'Run a headless Ghidra session with Python (PyGhidra) scripts:'
Write-Host "  & '$venvPython' -m pyghidra.ghidra_launch --install-dir '$GhidraDir' ghidra.app.util.headless.AnalyzeHeadless ``"
Write-Host '      <project dir> <project name> -process <program> -noanalysis -scriptPath re\ghidra\scripts -postScript <script>.py'
Write-Host ''
Write-Host 'Or rebuild the whole project with re\ghidra\build.ps1, which uses this venv.'
Write-Host 'JDK: Ghidra finds it through its saved java_home.save; set JAVA_HOME_OVERRIDE for one call to force another.'
