# Packs the tracked svmbuild sources, reviewed OpenSCCT art and the target platform's offline Python runtime.
# Only the release machine needs Python and pip.
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [ValidateSet('win-x64', 'linux-x64')][string]$Platform = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$python = if ($IsWindows) { 'python' } else { 'python3' }
& $python (Join-Path $PSScriptRoot 'pack_content_tools.py') $OutDir $Platform
if ($LASTEXITCODE -ne 0) { throw "Building content tools for $Platform failed." }
