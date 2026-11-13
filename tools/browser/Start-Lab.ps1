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
    $hostOutput = Join-Path $repo ("local/browser-host/" + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
    & dotnet publish tools/browser-lab -c Release --no-restore --output $hostOutput
    if ($LASTEXITCODE) { throw 'Browser lab publish failed.' }
    & dotnet (Join-Path $hostOutput 'OpenScct.BrowserLab.dll') $bundle
} finally { Pop-Location }
