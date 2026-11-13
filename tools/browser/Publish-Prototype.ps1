[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$buildId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$bundle = Join-Path $repo "local/browser-build/$buildId"
Push-Location $repo
try {
    & dotnet publish game/OpenScctGame.web/OpenScctGame.web.csproj -c Release --no-restore "-p:WasmAppDir=$bundle/"
    if ($LASTEXITCODE) { throw 'Browser publish failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $bundle 'index.html'))) { throw 'The browser bundle was not produced.' }
    Set-Content -LiteralPath (Join-Path $repo 'local/browser-build/current.txt') -Value $bundle -Encoding utf8
    Write-Host "Browser bundle: $bundle"
} finally { Pop-Location }
