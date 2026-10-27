<#
.SYNOPSIS
    Runs one or more transfer scripts headless on a program of the transfer working copy of the
    Ghidra project (local/ghidra-work/transfer), never on the shared project in local/ghidra.

.EXAMPLE
    re\ghidra\scripts\transfer\run.ps1 -Program 'SCCT Versus' -Scripts T10_export_features.py -ReadOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Program,
    [string[]]$Scripts = @(),
    [string[]]$ScriptArgs = @(),
    [switch]$ReadOnly,
    [string]$ProjectDir,
    [string]$ProjectName = 'SVM',
    [string]$Import,
    [string]$MaxHeap = '3G',
    [string]$GhidraDir = $(if ($env:GHIDRA_INSTALL_DIR) { $env:GHIDRA_INSTALL_DIR } else { (Join-Path $env:USERPROFILE 'Desktop\ghidra_12.2_PUBLIC') })
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$reDir = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $here))
$repo = Split-Path -Parent $reDir
if (-not $ProjectDir) { $ProjectDir = Join-Path $repo 'local\ghidra-work\transfer' }
$venvPython = Join-Path $reDir '.venv-ghidra\Scripts\python.exe'
$logDir = Join-Path $ProjectDir 'logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $logDir ("transfer-$stamp")

$hargs = @()
if ($Import) {
    $hargs += @('-import', $Import, '-overwrite', '-loader', 'PeLoader', '-processor', 'x86:LE:32:default', '-cspec', 'windows')
} else {
    $hargs += @('-process', $Program, '-noanalysis')
}
if ($ReadOnly) { $hargs += '-readOnly' }
foreach ($s in $Scripts) {
    $hargs += @('-postScript', $s)
    $hargs += $ScriptArgs
}
$all = @('-m', 'pyghidra.ghidra_launch', '--install-dir', $GhidraDir, "-Xmx$MaxHeap",
         'ghidra.app.util.headless.AnalyzeHeadless', $ProjectDir, $ProjectName) + $hargs +
       @('-scriptPath', "$here;$(Split-Path -Parent $here)", '-log', "$log.app.log", '-scriptlog', "$log.script.log")
Write-Host ('analyzeHeadless ' + ($hargs -join ' '))
$saved = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $venvPython @all 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath "$log.console.log" | Select-String -Pattern 'features:|bsim:|transfer:|reloaded:|ERROR|Traceback|Error|REPORT' | ForEach-Object { $_.Line }
$code = $LASTEXITCODE
$ErrorActionPreference = $saved
Write-Host "exit $code, log $log.console.log"
