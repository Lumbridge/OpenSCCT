<#
.SYNOPSIS
    Writes the shader lists a build carries (game/LoadShaders/<map>.json.gz), one load of each map.

.DESCRIPTION
    A map's load makes its shaders on worker threads from a list of the shader codes its last load asked for
    (ResponsiveSmokeShaders.Prewarm). A machine that has never loaded the map has no list of its own and reads the
    one the build carries. This script makes those: it starts an exported game once per map with
    SVM_SHADER_LIST_OUT set, which writes the list when the load ends and closes the game.

    The game runs with first-run settings (an empty user folder under -Work, through APPDATA), so the lists are those
    of a new player. Its window is off screen and never takes focus (--svm-background, and an override.cfg next to
    the exe for the moment before the game's own code runs). Run it against the export of the commit being released,
    then export again so the build holds the lists.

.PARAMETER Exe
    The exported game (OpenSCCT.exe), built from the commit the lists are for.

.PARAMETER Content
    The content folder (the one that holds svmap/).

.PARAMETER Out
    Where the lists go. Defaults to game/LoadShaders of this checkout.

.PARAMETER Maps
    Map ids to bake. Defaults to every folder of <Content>/svmap.

.PARAMETER Work
    A scratch folder for the empty user folder. Defaults to local/bake-shader-lists.

.PARAMETER TimeoutSeconds
    How long one map may take before its game is stopped and the map reported as failed.
#>
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Content,
    [string]$Out,
    [string[]]$Maps = @(),
    [string]$Work,
    [ValidateRange(0, 63)][Nullable[int]]$GpuIndex,
    [ValidateRange(30, 3600)][int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$contentPath = (Resolve-Path -LiteralPath $Content).Path
if (-not $Out) { $Out = Join-Path $repo 'game\LoadShaders' }
if (-not $Work) { $Work = Join-Path $repo 'local\bake-shader-lists' }
New-Item -ItemType Directory -Force -Path $Out, $Work | Out-Null
$outPath = (Resolve-Path -LiteralPath $Out).Path
$workPath = (Resolve-Path -LiteralPath $Work).Path
if ($Maps.Count -eq 0) {
    $Maps = @(Get-ChildItem -LiteralPath (Join-Path $contentPath 'svmap') -Directory | ForEach-Object Name | Sort-Object)
}

$override = Join-Path (Split-Path $exePath) 'override.cfg'
$previousOverride = if (Test-Path -LiteralPath $override) { [IO.File]::ReadAllBytes($override) } else { $null }
$keys = @('SVM_SHADER_LIST_OUT', 'SVM_AUDIO_MUTE', 'APPDATA')
$previous = @{}
foreach ($key in $keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key) }
$failed = @()
try {
    # Minimised and unfocused before the game's own code runs; --svm-background then keeps the window off screen.
    [IO.File]::WriteAllText($override, "[display]`n`nwindow/size/no_focus=true`nwindow/size/mode=1`n")
    $env:SVM_SHADER_LIST_OUT = $outPath
    $env:SVM_AUDIO_MUTE = '1'
    $env:APPDATA = $workPath
    foreach ($map in $Maps) {
        $list = Join-Path $outPath "$map.json.gz"
        Remove-Item -LiteralPath $list -ErrorAction SilentlyContinue
        $arguments = @()
        if ($null -ne $GpuIndex) { $arguments += @('--gpu-index', "$GpuIndex") }
        $arguments += @('--', '--svm-background', "--svm-play=$map", '--side=spy', '--rules=playlevel', '--bots=0',
            ('"--svm-content=' + $contentPath + '"'))
        $log = Join-Path $workPath "$map.log"
        $process = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $log -RedirectStandardError (Join-Path $workPath "$map.err")
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }

        if (Test-Path -LiteralPath $list) {
            Write-Host ("{0}: {1:N0} bytes" -f $map, (Get-Item -LiteralPath $list).Length)
        }
        else {
            Write-Host "${map}: no list written (see $log)"
            $failed += $map
        }
    }
}
finally {
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
    if ($null -ne $previousOverride) { [IO.File]::WriteAllBytes($override, $previousOverride) }
    else { Remove-Item -LiteralPath $override -ErrorAction SilentlyContinue }
}

if ($failed.Count -ne 0) {
    Write-Host "Failed: $($failed -join ', ')"
    exit 1
}
