[CmdletBinding()]
param([Parameter(Mandatory)][string]$ContentRoot, [string]$SteeEFolder)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repo
try {
    $nugetConfig = & "$PSScriptRoot/Get-TwoDog.ps1"
    & dotnet restore src/OpenScct.Server.Cli --configfile $nugetConfig
    if ($LASTEXITCODE) { throw 'Navigation tool restore failed.' }
    $map = Join-Path $repo 'local/browser-map/TestRange'
    New-Item -ItemType Directory -Path $map -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $ContentRoot 'svmap/TestRange') | Copy-Item -Destination $map -Recurse -Force
    & dotnet run --project src/OpenScct.Server.Cli -c Release --no-restore --no-launch-profile -- --bake-bot-nav $map
    if ($LASTEXITCODE) { throw 'Navigation preparation failed.' }
    $mapSources = if ($SteeEFolder) { @('--map-source', "SteeE=$SteeEFolder") } else { @() }
    & python tools/browser/prepare_content.py --content-root $ContentRoot --map-folder $map --output game/OpenScctGame.web/wwwroot/content.zip @mapSources
    if ($LASTEXITCODE) { throw 'Content packaging failed.' }
    & dotnet restore game/OpenScctGame.web/OpenScctGame.web.csproj --configfile $nugetConfig
    if ($LASTEXITCODE) { throw 'Browser package restore failed.' }
    & "$PSScriptRoot/Publish-Prototype.ps1"
    if ($LASTEXITCODE) { throw 'Browser publish failed.' }
} finally { Pop-Location }
