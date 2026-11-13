[CmdletBinding()]
param([switch]$AheadOfTime, [switch]$Interpreter, [switch]$FastLink = $true)
$ErrorActionPreference = 'Stop'
if ($AheadOfTime -and $Interpreter) { throw 'Choose AOT or the interpreter, not both.' }
$AheadOfTime = !$Interpreter
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$buildId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
if ($AheadOfTime) { $buildId += '-aot' }
$bundle = Join-Path $repo "local/browser-build/$buildId"
Push-Location $repo
try {
    $sourceCommit = (& git rev-parse HEAD).Trim()
    $sourceDirty = [bool]((& git status --porcelain --untracked-files=normal) -join '')
    [string[]]$compilerOptions = @()
    if ($AheadOfTime) { $compilerOptions += '-p:RunAOTCompilation=true' }
    # Keep release runtime semantics while using the smaller whole-module optimization pass.
    # The SDK's per-assembly AOT bitcode optimization remains -O2.
    if ($FastLink) { $compilerOptions += '-p:EmccLinkOptimizationFlag=-O1' }
    & dotnet publish game/OpenScctGame.web/OpenScctGame.web.csproj -c Release --no-restore "-p:WasmAppDir=$bundle/" @compilerOptions
    if ($LASTEXITCODE) { throw 'Browser publish failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $bundle 'index.html'))) { throw 'The browser bundle was not produced.' }
    @{
        build = $buildId
        sourceCommit = $sourceCommit
        sourceDirty = $sourceDirty
        aheadOfTime = [bool]$AheadOfTime
        linkOptimization = $(if ($FastLink) { 'O1' } else { 'O2' })
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'build.json') -Encoding utf8
    Set-Content -LiteralPath (Join-Path $repo 'local/browser-build/current.txt') -Value $bundle -Encoding utf8
    Write-Host "Browser bundle: $bundle"
} finally { Pop-Location }
