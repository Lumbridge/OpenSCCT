[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$bundle = (Get-Content -LiteralPath (Join-Path $repo 'local/browser-build/current.txt') -Raw).Trim()
if (!(Test-Path -LiteralPath (Join-Path $bundle 'index.html'))) { throw 'Build the browser prototype first.' }
Push-Location $repo
try {
    & dotnet restore tools/browser-lab --configfile (Join-Path $repo 'local/nuget-browser.config')
    if ($LASTEXITCODE) { throw 'Browser lab restore failed.' }
    & dotnet run --project tools/browser-lab -c Release --no-restore -- $bundle
} finally { Pop-Location }
