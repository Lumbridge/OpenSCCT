[CmdletBinding()]
param(
    [string]$Godot,
    [string]$OutDir,
    [string]$DependencySourceRoot,
    [string]$SteamAudioSdk,
    [switch]$SkipBuild,
    [switch]$WithContent
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$Godot) { $Godot = & (Join-Path $repo 'tools/Get-Godot.ps1') | Select-Object -Last 1 }
$Godot = [IO.Path]::GetFullPath($Godot)
if (!$OutDir) { $OutDir = Join-Path $repo 'local/audio-modernization/validation' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$oldCapture = $env:SVM_AUDIO_CAPTURE_DIR
$oldReverb = $env:SVM_REVERB_CAPTURE_DIR
$oldLog = $env:SVM_AUDIO_LOG
Push-Location $repo
try {
    if (!$SkipBuild) {
        if ($IsLinux) {
            $previousGodot = $env:GODOT
            $previousDeps = $env:SCCT_AUDIO_DEPS_DIR
            $previousSdk = $env:SCCT_STEAMAUDIO_SDK
            try {
                $env:GODOT = $Godot
                $env:SCCT_AUDIO_DEPS_DIR = $DependencySourceRoot
                $env:SCCT_STEAMAUDIO_SDK = $SteamAudioSdk
                & bash (Join-Path $PSScriptRoot 'build-native-audio.sh')
            } finally {
                $env:GODOT = $previousGodot
                $env:SCCT_AUDIO_DEPS_DIR = $previousDeps
                $env:SCCT_STEAMAUDIO_SDK = $previousSdk
            }
        } else {
            & (Join-Path $PSScriptRoot 'Build-NativeAudio.ps1') -Godot $Godot -DependencySourceRoot $DependencySourceRoot -SteamAudioSdk $SteamAudioSdk
        }
        if ($LASTEXITCODE) { throw 'Native audio build failed.' }
        & dotnet build game/OpenScctGame.csproj --nologo -v:q
        if ($LASTEXITCODE) { throw 'Game build failed.' }
        & $Godot --headless --editor --import --path game --quit-after 120 *> (Join-Path $OutDir 'import.log')
        if ($LASTEXITCODE) { throw 'Godot import failed.' }
    }
    $native = if ($IsLinux) { 'game/addons/scct_audio/bin/linux-x86_64/libscct_audio.so' }
        else { 'game/addons/scct_audio/bin/windows-x86_64/scct_audio.dll' }
    & python (Join-Path $PSScriptRoot 'test-binaural-dsp.py') --library $native --out (Join-Path $OutDir 'binaural-dsp.json')
    if ($LASTEXITCODE) { throw 'Native binaural DSP checks failed.' }
    $checks.Add(@{ name='binaural-dsp'; passed=$true })
    $suites = @(
        @{ project='tests/OpenScctGame.Logic.Tests'; filter='FullyQualifiedName~Acoustic|FullyQualifiedName~GameSettingsTests|FullyQualifiedName~AudioRouter|FullyQualifiedName~EaxEnvironment|FullyQualifiedName~SoundPriority' },
        @{ project='tests/OpenScct.Game.Tests'; filter='FullyQualifiedName~AiPerceptionTests|FullyQualifiedName~SoundDeviceTests|FullyQualifiedName~ReloadAudioTests|FullyQualifiedName~ShockerLaserAudioTests' },
        @{ project='tests/OpenScct.Integration.Tests'; filter='FullyQualifiedName~PawnSoundReplicationTests|FullyQualifiedName~AudioParityRoutingTests' }
    )
    foreach ($suite in $suites) {
        & dotnet test $suite.project --nologo -v:q --filter $suite.filter
        if ($LASTEXITCODE) { throw "Audio regression suite failed: $($suite.project)" }
        $checks.Add(@{ name=$suite.project; passed=$true })
    }
    $env:SVM_AUDIO_CAPTURE_DIR = $OutDir
    $env:SVM_REVERB_CAPTURE_DIR = Join-Path $OutDir 'reverb'
    $env:SVM_AUDIO_LOG = '1'
    $smokes = @('audio-modernization','audio-propagation','audio-adaptive-reverb','reverb')
    if ($WithContent) { $smokes += @('audio','audio-listener','audio-aquarius','audio-aquarius-tunnel','audio-gameplay-propagation','play-audio-merc','play-audio') }
    $driver = if ($IsLinux) { 'PulseAudio' } else { 'WASAPI' }
    foreach ($smoke in $smokes) {
        $log = Join-Path $OutDir "$smoke.log"
        & $Godot --headless --audio-driver $driver --path game -- "--svm-smoke=$smoke" --svm-smoke-timeout=120 --svm-setting=audio_output_mode=Headphones *> $log
        $passed = $LASTEXITCODE -eq 0 -and (Select-String -LiteralPath $log -SimpleMatch "SVM_SMOKE $smoke OK" -Quiet) -and
            !(Select-String -LiteralPath $log -Pattern '^ERROR:|^SCRIPT ERROR:' -Quiet)
        $checks.Add(@{ name=$smoke; passed=$passed; log=$log })
        if (!$passed) { throw "Audio smoke failed: $smoke (see $log)" }
        Write-Host "$smoke passed"
    }
} finally {
    $env:SVM_AUDIO_CAPTURE_DIR = $oldCapture
    $env:SVM_REVERB_CAPTURE_DIR = $oldReverb
    $env:SVM_AUDIO_LOG = $oldLog
    $checks | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutDir 'checks.json') -Encoding utf8NoBOM
    Pop-Location
}
