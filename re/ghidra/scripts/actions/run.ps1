<#
.SYNOPSIS
    Runs one of the scripts in re/ghidra/scripts/actions headless on the working copy
    local/ghidra-work/actions (never on the shared project in local/ghidra).

.EXAMPLE
    re\ghidra\scripts\actions\run.ps1 -Program 'ChaosTheory_Editor.exe' -Script a20_decomp.py -ScriptArgs job.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Program,
    [Parameter(Mandatory)][string]$Script,
    [string[]]$ScriptArgs = @(),
    [string]$ProjectDir = '',
    [string]$MaxHeap = '3g',
    [switch]$ReadOnly
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
if (-not $ProjectDir) { $ProjectDir = Join-Path $repo 'local\ghidra-work\actions' }
$shared = Join-Path $repo 'local\ghidra'
if ((Resolve-Path $ProjectDir).Path -eq (Resolve-Path $shared).Path) { throw 'refusing to run on the shared project' }
$ghidra = if ($env:GHIDRA_INSTALL_DIR) { $env:GHIDRA_INSTALL_DIR } else { (Join-Path $env:USERPROFILE 'Desktop\ghidra_12.2_PUBLIC') }
$py = Join-Path $repo 're\.venv-ghidra\Scripts\python.exe'
$logDir = Join-Path $repo 'local\re-out\c\actions\logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$stem = [IO.Path]::GetFileNameWithoutExtension($Script) + '-' + ($Program -replace '[^A-Za-z0-9]', '_')
$all = @('-m', 'pyghidra.ghidra_launch', '--install-dir', $ghidra, "-Xmx$MaxHeap",
         'ghidra.app.util.headless.AnalyzeHeadless', $ProjectDir, 'SVM', '-process', $Program, '-noanalysis',
         '-scriptPath', $here, '-postScript', $Script) + $ScriptArgs +
       @('-log', (Join-Path $logDir "$stem.app.log"), '-scriptlog', (Join-Path $logDir "$stem.script.log"))
if ($ReadOnly) { $all += '-readOnly' }
$env:MAXMEM = $MaxHeap
$saved = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $py @all 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath (Join-Path $logDir "$stem.console.log") |
    Select-String -Pattern 'SVM |ERROR|Traceback|Exception' | ForEach-Object { $_.Line }
$code = $LASTEXITCODE
$ErrorActionPreference = $saved
exit $code
