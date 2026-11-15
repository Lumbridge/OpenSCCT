[CmdletBinding()]
param([switch]$ManagedOnly)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$feed = Join-Path $repo 'local/2dog-packages'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
$release = '4.7.2.186'
$native = '4.7.2.175'
$base = "https://github.com/outfox/2dog/releases/download/v$release"
$manifest = Invoke-RestMethod "$base/release-manifest.json"
$names = @("2dog.engine.$release.nupkg", "2dog.godotsharp.$release.nupkg",
    "2dog.godotsharp.editor.$release.nupkg", "2dog.tools.$native.nupkg")
if (!$ManagedOnly) {
    foreach ($platform in @('win-x64', 'linux-x64', 'osx-arm64')) {
        $names += "2dog.$platform.$native.nupkg"
        foreach ($variant in @('debug', 'release', 'editor')) { $names += "2dog.$platform.$variant.$native.nupkg" }
    }
    $names += "2dog.browser-wasm.$native.nupkg", "2dog.browser-wasm.debug.$native.nupkg", "2dog.browser-wasm.release.$native.nupkg"
}
foreach ($name in $names) {
    $expected = $manifest.files.$name
    if ($expected -notmatch '^[a-f0-9]{64}$') { throw "No release digest for $name" }
    $target = Join-Path $feed $name
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $expected) { continue }
    Write-Host "Downloading $name"
    $partial = "$target.partial"
    Invoke-WebRequest "$base/$name" -OutFile $partial
    if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $expected) { throw "Digest mismatch for $name" }
    Move-Item -LiteralPath $partial -Destination $target -Force
}
$escaped = [System.Security.SecurityElement]::Escape($feed)
$config = Join-Path $repo 'local/nuget-browser.config'
@"
<configuration><packageSources><clear />
<add key="2dog-release" value="$escaped" />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
"@ | Set-Content -LiteralPath $config
Write-Output $config
