[CmdletBinding()]
param(
    [string]$Godot,
    [string]$BuildDir,
    [string]$DependencySourceRoot,
    [ValidateSet('OPENAL', 'HRVB')][string]$Backend = 'HRVB',
    [switch]$SkipStage,
    [int]$Jobs = 4
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$BuildDir) { $BuildDir = Join-Path $repo 'local/audio-native/windows-x86_64' }
$BuildDir = [IO.Path]::GetFullPath($BuildDir)
if (!$Godot) { $Godot = & (Join-Path $repo 'tools/Get-Godot.ps1') | Select-Object -Last 1 }
$Godot = [IO.Path]::GetFullPath($Godot)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Install Visual Studio C++ build tools (x64) to build native audio.' }
$vs = $vs.Trim()
$cmake = Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ninja = Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe'
if (!(Test-Path -LiteralPath $cmake)) { $cmake = (Get-Command cmake -ErrorAction Stop).Source }
if (!(Test-Path -LiteralPath $ninja)) { $ninja = (Get-Command ninja -ErrorAction Stop).Source }
# Import the compiler environment into this process; nothing changes in the user's saved environment.
$dev = Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
$compilerEnv = & $env:ComSpec /d /s /c "`"$dev`" -no_logo -arch=x64 -host_arch=x64 >nul && set"
if ($LASTEXITCODE) { throw 'Visual Studio compiler environment failed.' }
foreach ($line in $compilerEnv) {
    if ($line -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process') }
}
New-Item -ItemType Directory -Force $BuildDir | Out-Null
$apiDumpDir = Join-Path $BuildDir 'api-dump'
New-Item -ItemType Directory -Force $apiDumpDir | Out-Null
Push-Location $apiDumpDir
try {
    & $Godot --headless --dump-extension-api
    if ($LASTEXITCODE) { throw 'Godot API dump failed.' }
} finally { Pop-Location }
$apiSource = Join-Path $apiDumpDir 'extension_api.json'
$apiTarget = Join-Path $BuildDir 'extension_api.json'
if (!(Test-Path -LiteralPath $apiTarget) -or (Get-FileHash $apiSource).Hash -ne (Get-FileHash $apiTarget).Hash) {
    Copy-Item -LiteralPath $apiSource -Destination $apiTarget -Force
}
$dependencyArgs = @()
if ($DependencySourceRoot) {
    $dependencyArgs = @("-DFETCHCONTENT_SOURCE_DIR_GODOTCPP=$([IO.Path]::GetFullPath((Join-Path $DependencySourceRoot 'godot-cpp')))",
        "-DFETCHCONTENT_SOURCE_DIR_OPENAL=$([IO.Path]::GetFullPath((Join-Path $DependencySourceRoot 'openal-soft')))",
        "-DFETCHCONTENT_SOURCE_DIR_REAL=$([IO.Path]::GetFullPath((Join-Path $DependencySourceRoot 'real')))")
}
& $cmake -S (Join-Path $repo 'src/OpenScct.Audio.Native') -B $BuildDir -G Ninja `
    "-DCMAKE_MAKE_PROGRAM=$ninja" -DCMAKE_BUILD_TYPE=Release `
    "-DGODOTCPP_CUSTOM_API_FILE=$(Join-Path $BuildDir 'extension_api.json')" "-DSCCT_AUDIO_REVERB_BACKEND=$Backend" @dependencyArgs
if ($LASTEXITCODE) { throw 'Native audio configuration failed.' }
& $cmake --build $BuildDir --parallel $Jobs
if ($LASTEXITCODE) { throw 'Native audio build failed.' }
if (!$SkipStage) {
    & $cmake --install $BuildDir --prefix (Join-Path $repo 'game/addons/scct_audio')
    if ($LASTEXITCODE) { throw 'Native audio staging failed.' }
}
