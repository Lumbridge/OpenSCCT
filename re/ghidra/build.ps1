<#
.SYNOPSIS
    Builds the local Ghidra project local/ghidra/SVM from the locked binaries and runs the
    annotation scripts. Safe to re-run.

.DESCRIPTION
    For each target (editor first, then game):
      1. Import and auto-analyse, only when the target has no analysis marker yet (or -Reanalyze).
         Right after analysis it records the analysis baseline (01_baseline.py) and applies the
         tracked annotations from re/ghidra/annotations/<id>.json if present (05_apply_annotations.py).
      2. Re-open the program with -process -noanalysis and run the target's post-scripts in order,
         ending with 30_export.py, which rewrites the tracked annotations JSON.
    Every binary is checked against re/binaries.lock.json first; any other build is refused.

.PARAMETER Targets
    Which programs to build: editor, game, or both (default, in that order).

.PARAMETER Reanalyze
    Re-import and re-analyse even if the target was analysed before (overwrites the program).

.PARAMETER AnalysisOnly
    Run only step 1.

.PARAMETER ScriptsOnly
    Run only step 2. The target must have been analysed.

.PARAMETER Project
    Project directory and name (default local/ghidra and SVM).
#>
[CmdletBinding()]
param(
    [ValidateSet('editor', 'game')]
    [string[]]$Targets = @('editor', 'game'),
    [switch]$Reanalyze,
    [switch]$AnalysisOnly,
    [switch]$ScriptsOnly,
    [string]$GhidraDir = $(if ($env:GHIDRA_INSTALL_DIR) { $env:GHIDRA_INSTALL_DIR } else { (Join-Path $env:USERPROFILE 'Desktop\ghidra_12.2_PUBLIC') }),
    [string]$GameDir = $(if ($env:SVM_GAME_DIR) { $env:SVM_GAME_DIR } else { (Join-Path $env:USERPROFILE 'Desktop\Enhanced SCCT Versus v4.0 Public Beta 3') }),
    [string]$ProjectDir,
    [string]$ProjectName = 'SVM',
    [string]$MaxHeap = '24g'
)

$ErrorActionPreference = 'Stop'
$reDir = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent $reDir
if (-not $ProjectDir) { $ProjectDir = Join-Path $repo 'local\ghidra' }
$scripts = Join-Path $PSScriptRoot 'scripts'
$logDir = Join-Path $ProjectDir 'logs'
$markerDir = Join-Path $ProjectDir 'analysis'
$venvPython = Join-Path $reDir '.venv-ghidra\Scripts\python.exe'

# Program name in the project is the file name, as Ghidra's importer sets it.
$targetInfo = @{
    editor = @{ File = 'System\ChaosTheory_Editor.exe'; Program = 'ChaosTheory_Editor.exe'
                Scripts = @('20_editor_guards.py', '21_editor_natives.py', '30_export.py') }
    game   = @{ File = 'System\SCCT Versus'; Program = 'SCCT Versus'
                Scripts = @('12_natives.py', '13_statics_vtables.py', '30_export.py') }
}

New-Item -ItemType Directory -Force $ProjectDir, $logDir, $markerDir | Out-Null

if (-not (Test-Path $venvPython)) {
    & (Join-Path $reDir 'tools\ghidra-env.ps1') -GhidraDir $GhidraDir
}

Write-Host 'Checking binaries against re/binaries.lock.json'
& $venvPython -I (Join-Path $reDir 'tools\lock_binaries.py') --check --install $GameDir
if ($LASTEXITCODE -ne 0) { throw 'The install does not match re/binaries.lock.json; refusing to build.' }

$env:SVM_GAME_DIR = $GameDir
# The editor goes first: 21_editor_natives.py writes the native table 12_natives.py reads.
$Targets = @('editor', 'game') | Where-Object { $Targets -contains $_ }

function Invoke-Headless([string[]]$HeadlessArgs, [string]$Log) {
    $all = @('-m', 'pyghidra.ghidra_launch', '--install-dir', $GhidraDir, "-Xmx$MaxHeap",
             'ghidra.app.util.headless.AnalyzeHeadless', $ProjectDir, $ProjectName) + $HeadlessArgs +
           @('-scriptPath', $scripts, '-log', "$Log.app.log", '-scriptlog', "$Log.script.log")
    Write-Host ('analyzeHeadless ' + ($HeadlessArgs -join ' '))
    # Ghidra logs to stderr; keep that from turning into PowerShell errors.
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $venvPython @all 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath "$Log.console.log" | Out-Host
    $code = $LASTEXITCODE
    $ErrorActionPreference = $saved
    $console = Get-Content -Raw "$Log.console.log"
    if ($code -ne 0) { throw "analyzeHeadless failed with exit code $code (see $Log.console.log)" }
    if ($console -match 'SCRIPT ERROR|Traceback \(most recent call last\)|REPORT: Script .* failed') {
        throw "a script failed (see $Log.console.log)"
    }
}

foreach ($t in $Targets) {
    $info = $targetInfo[$t]
    $binary = Join-Path $GameDir $info.File
    $marker = Join-Path $markerDir "$t.json"

    if (-not $ScriptsOnly -and ($Reanalyze -or -not (Test-Path $marker))) {
        $start = Get-Date
        $stats = Join-Path $repo "local\re-out\$t.analysis-stats.json"
        Remove-Item -Force -ErrorAction SilentlyContinue $stats, $marker
        Invoke-Headless -Log (Join-Path $logDir "$t-analysis") -HeadlessArgs @(
            '-import', $binary, '-overwrite',
            '-loader', 'PeLoader', '-processor', 'x86:LE:32:default', '-cspec', 'windows',
            '-postScript', '01_baseline.py', '-postScript', '05_apply_annotations.py')
        $seconds = [int]((Get-Date) - $start).TotalSeconds
        if (-not (Test-Path $stats)) { throw "analysis finished but $stats was not written" }
        @{ target = $t; program = $info.Program; seconds = $seconds; finished = (Get-Date).ToString('s') } |
            ConvertTo-Json | Set-Content -Encoding utf8 $marker
        Write-Host "$t analysed in $seconds s"
    }

    if (-not $AnalysisOnly) {
        if (-not (Test-Path $marker)) { throw "$t has not been analysed yet; run without -ScriptsOnly first" }
        $scriptArgs = @()
        foreach ($s in $info.Scripts) { $scriptArgs += @('-postScript', $s) }
        Invoke-Headless -Log (Join-Path $logDir "$t-scripts") -HeadlessArgs (@('-process', $info.Program, '-noanalysis') + $scriptArgs)
    }
}
Write-Host 'Done.'
