#Requires -Version 7
<#
Capture controlled impulses through the owner's Creative ALchemy DirectSound/EAX DLL.
The harness uses only process-loopback for its own PID. It never opens a microphone,
records another app, changes the system volume, or writes the original installation.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OriginalSystem,
    [string]$Content = (Join-Path $PSScriptRoot '../../.content'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../local/reverb-parity/vendor-reference-captures'),
    [ValidateRange(1, 30)][double]$Seconds = 8,
    [string[]]$Cases
)
$ErrorActionPreference = 'Stop'
$OriginalSystem = (Resolve-Path -LiteralPath $OriginalSystem).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$vendor = Join-Path $OutputDirectory 'vendor'
New-Item -ItemType Directory -Force $vendor | Out-Null
$identities = @()
foreach ($name in @('dsound.dll', 'eax.dll', 'dsound.ini')) {
    $source = Join-Path $OriginalSystem $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing original vendor file: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $vendor $name)
    $identities += [ordered]@{ name = $name; source = $source; sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$probeDir = Join-Path $OutputDirectory 'probe'
& (Join-Path $PSScriptRoot 'Build-OriginalEaxProbe.ps1') -OutputDirectory $probeDir | Write-Host
$probe = Join-Path $probeDir 'Probe-OriginalEax.exe'
$paramsDir = Join-Path $OutputDirectory 'parameters'
& python (Join-Path $PSScriptRoot 'reverb_reference.py') --content $Content --out $paramsDir | Set-Content -LiteralPath (Join-Path $OutputDirectory 'parameter-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'Could not build source parameter fixtures.' }
$params = Get-Content -LiteralPath (Join-Path $paramsDir 'parameters.json') -Raw | ConvertFrom-Json
$manifest = [ordered]@{ format = 'openscct.original-eax-capture/1'; vendor = $identities; capture = 'self-process-tree-only'; microphone = $false; sampleRate = 48000; channels = 2; pcm = 'float32le'; inputImpulse = [ordered]@{ amplitude = (3277.0/32768.0); frame = 12000 }; cases = @() }
Push-Location $OutputDirectory
try {
    foreach ($fixture in $params.fixtures) {
        if ($Cases -and $fixture.name -notin $Cases) { continue }
        $wav = Join-Path $OutputDirectory ($fixture.name + '.vendor.wav')
        $log = Join-Path $OutputDirectory ($fixture.name + '.vendor.json')
        $raw = & $probe (Join-Path $vendor 'dsound.dll') $fixture.blockFile $wav $Seconds
        $exit = $LASTEXITCODE
        $raw | Set-Content -LiteralPath $log
        if ($exit -ne 0) { throw "Original EAX probe failed for $($fixture.name), exit $exit; see $log" }
        $result = $raw | ConvertFrom-Json
        if (-not $result.audioPlayed -or $result.capturedPeak -le 0 -or $result.returnedDifferentBytes -ne 0) { throw "No verified original playback for $($fixture.name): $log" }
        $manifest.cases += [ordered]@{ name = $fixture.name; wav = $wav; log = $log; sha256 = (Get-FileHash -LiteralPath $wav -Algorithm SHA256).Hash.ToLowerInvariant(); result = $result }
        $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'capture-manifest.json')
        Write-Host "$($fixture.name): $($result.capturedFrames) frames, peak $($result.capturedPeak), EAX112-byte exact round trip"
    }
}
finally { Pop-Location }
