<#
.SYNOPSIS
Runs named real-process acceptance cases serially and preserves every result.
.EXAMPLE
pwsh tools/network-lab-matrix.ps1 -GameExe C:/build/OpenSCCT.console.exe -SkipBuild
.EXAMPLE
pwsh tools/network-lab-matrix.ps1 -List
#>
[CmdletBinding()]
param(
    [string]$GameExe,
    [string]$DedicatedExe,
    [string[]]$Case,
    [switch]$SkipBuild,
    [switch]$List,
    [switch]$DryRun,
    [ValidateRange(0,1440)][int]$MaxMinutes = 0
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
$repo = Split-Path -Parent $PSScriptRoot
$catalog = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'network-lab-matrix.json') -Raw | ConvertFrom-Json -AsHashtable
$cases = @($catalog.cases)
if ($Case) {
    foreach ($name in $Case) {
        if ($name -notin $cases.name) { throw "Unknown case '$name'. Use -List to see available cases." }
    }
    $cases = @($cases | Where-Object name -in $Case)
}
if ($List) { $cases | ForEach-Object { "{0}: {1}" -f $_.name, $_.description }; return }
if ($GameExe) { $GameExe = (Resolve-Path -LiteralPath $GameExe).Path }
if ($DedicatedExe) {
    if (!$SkipBuild) { throw 'An explicit -DedicatedExe requires -SkipBuild.' }
    $DedicatedExe = (Resolve-Path -LiteralPath $DedicatedExe).Path
}
if ($DryRun) {
    $cases | ForEach-Object { @{name=$_.name;parameters=$_.parameters} | ConvertTo-Json -Depth 8 }
    return
}
if (!$SkipBuild) {
    if (!$GameExe) {
        & dotnet build (Join-Path $repo 'game/OpenScctGame.slnx') --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Game build failed.' }
    }
    if (@($cases | Where-Object { $_.parameters.ContainsKey('Dedicated') -and $_.parameters.Dedicated }).Count) {
        & dotnet build (Join-Path $PSScriptRoot 'network-lab-server/OpenScct.NetworkLab.Server.csproj') --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Dedicated helper build failed.' }
    }
}
$began = [DateTimeOffset]::UtcNow
$batch = Join-Path $repo ('local/network-lab/matrix-' + $began.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
$null = New-Item -ItemType Directory -Path $batch
$rows = [Collections.Generic.List[object]]::new()
function Save-Progress {
    @{started=$began;updated=[DateTimeOffset]::UtcNow;gameExe=$GameExe;dedicatedExe=$DedicatedExe;cases=$rows.ToArray()} |
        ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $batch 'matrix-result.json') -Encoding utf8
}
foreach ($item in $cases) {
    $row = @{name=$item.name;description=$item.description;parameters=$item.parameters;outcome='NOT RUN';error=$null;runDirectory=$null}
    $rows.Add($row)
    if ($MaxMinutes -gt 0 -and ([DateTimeOffset]::UtcNow-$began).TotalMinutes -ge $MaxMinutes) {
        $row.error = 'Matrix time budget reached before starting this case.'
        Save-Progress
        continue
    }
    $run = Join-Path $repo ('local/network-lab/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
    $row.runDirectory = $run
    $row.outcome = 'INCOMPLETE'
    Save-Progress
    $arguments = @{Action='Run';RunDirectory=$run;Windowed=$true;SkipBuild=$true}
    if ($GameExe) { $arguments.GameExe=$GameExe }
    if ($DedicatedExe) { $arguments.DedicatedExe=$DedicatedExe }
    foreach ($entry in $item.parameters.GetEnumerator()) {
        if ($arguments.ContainsKey($entry.Key)) { throw "Case '$($item.name)' overrides matrix-owned argument '$($entry.Key)'." }
        $arguments[$entry.Key]=$entry.Value
    }
    Write-Output "Running $($item.name): $run"
    try {
        & (Join-Path $PSScriptRoot 'network-lab.ps1') @arguments *> (Join-Path $batch ($item.name + '.log'))
        $resultPath = Join-Path $run 'result.json'
        if (!(Test-Path -LiteralPath $resultPath)) { throw 'Case returned without a conclusive result.json.' }
        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        if (!$result.passed) { throw "Case reported failure: $($result.error)" }
        $row.outcome = 'PASS'
    } catch {
        $row.outcome = 'FAIL'
        $row.error = $_.Exception.Message
    } finally { Save-Progress }
    Write-Output "$($row.outcome) $($item.name)"
}
Write-Output "Matrix evidence: $batch"
if (@($rows | Where-Object outcome -ne 'PASS').Count) { throw 'Matrix has failed, incomplete or unrun cases; see matrix-result.json.' }
