param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Cameras,
    [Parameter(Mandatory)][string]$Out,
    [string]$Map = 'SteeE',
    [ValidateSet('spy', 'merc')][string]$Side = 'spy',
    [double]$Seconds = 10,
    [int]$Bots = 0,
    [switch]$Gameplay,
    [switch]$Profiling,
    [ValidateSet('vulkan','d3d12')][string]$Driver = 'vulkan',
    [string]$Content = '.content'
)
$ErrorActionPreference = 'Stop'
if (-not ('FpsBenchmarkProcess' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FpsBenchmarkProcess {
    [StructLayout(LayoutKind.Sequential)] public struct PowerState { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool SetProcessInformation(IntPtr process, int kind, ref PowerState state, uint size);
    public static bool Unthrottle(IntPtr process) {
        var state = new PowerState { Version=1, ControlMask=1, StateMask=0 };
        return SetProcessInformation(process, 4, ref state, (uint)Marshal.SizeOf<PowerState>());
    }
}
'@
}
if (-not [double]::IsFinite($Seconds) -or $Seconds -le 0) { throw 'Seconds must be positive and finite' }
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$cameraPath = (Resolve-Path -LiteralPath $Cameras).Path
$outPath = [System.IO.Path]::GetFullPath($Out)
$contentPath = (Resolve-Path -LiteralPath $Content).Path
$cameraRecord = Get-Content -LiteralPath $cameraPath -Raw | ConvertFrom-Json -AsHashtable
if ($cameraRecord.map -ne $Map -or $cameraRecord.team -ne $Side) { throw 'Camera map/side does not match the selected play session' }
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
# The exported engine shows its window before managed code runs. Start minimised and unfocused, then the
# benchmark's BackgroundWindow restores it off screen for rendering. Do not change a normal installed build.
$override = Join-Path (Split-Path $exePath) 'override.cfg'
$previousOverride = if (Test-Path -LiteralPath $override) { [IO.File]::ReadAllBytes($override) } else { $null }
$keys = @('SVM_BENCHMARK','SVM_BENCHMARK_OUT','SVM_BENCHMARK_SECONDS','SVM_BENCHMARK_GAMEPLAY','SVM_BENCHMARK_PROFILING','SVM_AUDIO_MUTE')
$previous = @{}
foreach ($key in $keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key) }
try {
    $overrideText = "[display]`n`nwindow/size/no_focus=true`nwindow/size/mode=1`n"
    [IO.File]::WriteAllText($override, $overrideText)
    $env:SVM_BENCHMARK = $cameraPath
    $env:SVM_BENCHMARK_OUT = $outPath
    $env:SVM_BENCHMARK_SECONDS = $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:SVM_BENCHMARK_GAMEPLAY = if ($Gameplay) { '1' } else { '0' }
    $env:SVM_BENCHMARK_PROFILING = if ($Profiling) { '1' } else { '0' }
    $env:SVM_AUDIO_MUTE = '1'
    $arguments = @('--rendering-driver', $Driver)
    $arguments += @('--', '--svm-background', "--svm-play=$Map", "--side=$Side", '--rules=playlevel',
        "--bots=$Bots", ('"--svm-content=' + $contentPath + '"'))
    $process = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $outPath 'game.log') -RedirectStandardError (Join-Path $outPath 'game.err')
    if (-not [FpsBenchmarkProcess]::Unthrottle($process.Handle)) {
        Stop-Process -Id $process.Id -Force
        throw 'Could not disable background power throttling for the benchmark child'
    }
    if (-not $process.WaitForExit(600000)) {
        Stop-Process -Id $process.Id -Force
        throw 'Benchmark timed out'
    }
    if ($process.ExitCode -ne 0) { throw "Benchmark exited with $($process.ExitCode); see $outPath/game.err" }
    if (Select-String -LiteralPath (Join-Path $outPath 'game.err') -Pattern '^(SHADER ERROR|SCRIPT ERROR|ERROR):' -Quiet) {
        throw "Benchmark rendered with errors; results are invalid. See $outPath/game.err"
    }
    $dataPath = Join-Path (Split-Path $exePath) 'data_OpenScctGame_windows_x86_64'
    $dependencies = @{}
    foreach ($dll in Get-ChildItem -LiteralPath $dataPath -Filter '*.dll' | Where-Object { $_.Name -like 'OpenScct*' -or $_.Name -eq 'GodotSharp.dll' }) {
        $dependencies[$dll.Name] = (Get-FileHash -LiteralPath $dll.FullName).Hash
    }
    $pck = [IO.Path]::ChangeExtension($exePath, '.pck')
    @{
        exe = $exePath; exeSha256 = (Get-FileHash -LiteralPath $exePath).Hash
        assemblySha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exePath) 'data_OpenScctGame_windows_x86_64/OpenScctGame.dll')).Hash
        runtimeConfigSha256 = (Get-FileHash -LiteralPath (Join-Path (Split-Path $exePath) 'data_OpenScctGame_windows_x86_64/OpenScctGame.runtimeconfig.json')).Hash
        camerasSha256 = (Get-FileHash -LiteralPath $cameraPath).Hash
        pckSha256 = if (Test-Path -LiteralPath $pck) { (Get-FileHash -LiteralPath $pck).Hash } else { $null }
        dependencies = $dependencies
        capturedUtc = [DateTime]::UtcNow.ToString('o'); requestedDriver = $Driver
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outPath 'build.json')
    Get-Content -LiteralPath (Join-Path $outPath 'summary.json')
}
finally {
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
    if ($null -ne $previousOverride) { [IO.File]::WriteAllBytes($override, $previousOverride) }
    else { Remove-Item -LiteralPath $override -Force -ErrorAction SilentlyContinue }
}
