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
    [string]$Content = '.content',
    [ValidateRange(0, 63)][Nullable[int]]$GpuIndex,
    [string[]]$Settings = @(),
    # Extra engine options placed before '--' (for example --gpu-profile).
    [string[]]$EngineArgs = @(),
    # Project settings written to override.cfg as section/key=value (for example rendering/driver/depth_prepass/enable=false).
    [string[]]$ProjectOverrides = @(),
    [ValidateRange(0, 3600)][int]$QuietWaitSeconds = 120,
    [string[]]$IgnoreCompetitors = @()
)
$ErrorActionPreference = 'Stop'
# pwsh -File passes "a,b" as one string, so split comma lists here.
$Settings = @($Settings | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$IgnoreCompetitors = @($IgnoreCompetitors | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
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
if (-not ('FpsBenchmarkContention' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class FpsBenchmarkContention {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct ProcessEntry {
        public uint Size, Usage, ProcessId; public IntPtr DefaultHeapId; public uint ModuleId, Threads, ParentProcessId;
        public int PriorityBase; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=260)] public string ExeFile;
    }
    public sealed class Entry { public int Id; public int ParentId; public string Name; public string Path; }
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint="Process32FirstW", CharSet=CharSet.Unicode)] private static extern bool First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint="Process32NextW", CharSet=CharSet.Unicode)] private static extern bool Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", EntryPoint="QueryFullProcessImageNameW", CharSet=CharSet.Unicode)] private static extern bool ImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    // One cheap toolhelp snapshot: id, parent, exe name and (when readable) full path of every process.
    public static List<Entry> Snapshot() {
        var result = new List<Entry>();
        IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return result;
        try {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool ok = First(snapshot, ref entry); ok; ok = Next(snapshot, ref entry))
                result.Add(new Entry { Id = (int)entry.ProcessId, ParentId = (int)entry.ParentProcessId, Name = entry.ExeFile, Path = PathOf(entry.ProcessId) });
        }
        finally { CloseHandle(snapshot); }
        return result;
    }
    private static string PathOf(uint id) {
        IntPtr process = OpenProcess(0x1000, false, id);
        if (process == IntPtr.Zero) return null;
        try {
            var name = new StringBuilder(1024);
            uint size = (uint)name.Capacity;
            return ImageName(process, 0, name, ref size) ? name.ToString() : null;
        }
        finally { CloseHandle(process); }
    }
}
'@
}
# Another game, editor or renderer on this machine (another agent's benchmark, the original's sandbox) skews the GPU.
$competitorNames = @('OpenSCCT*', 'Godot*', 'ClassicGame*', 'SCCT*', 'SplinterCell*', 'UnrealEd*', 'SCCT_Versus*')
$sandboxRoot = 'F:\SCCT-Automation\'
function Get-Competitors([int[]]$OwnRoots) {
    $all = [FpsBenchmarkContention]::Snapshot()
    $parents = $null
    foreach ($p in $all) {
        if ($p.Id -eq 0) { continue }
        $named = $false
        foreach ($pattern in $competitorNames) { if ($p.Name -like $pattern) { $named = $true; break } }
        $sandboxed = $null -ne $p.Path -and $p.Path.StartsWith($sandboxRoot, [StringComparison]::OrdinalIgnoreCase)
        if (-not ($named -or $sandboxed)) { continue }
        $ignored = $false
        foreach ($pattern in $IgnoreCompetitors) { if ($p.Name -like $pattern) { $ignored = $true; break } }
        if ($ignored) { continue }
        # This script, the game it started and their children are not competitors.
        if ($null -eq $parents) {
            $parents = @{}
            foreach ($q in $all) { $parents[$q.Id] = $q.ParentId }
        }
        $own = $false
        $id = $p.Id
        for ($depth = 0; $depth -lt 8 -and $id -ne 0; $depth++) {
            if ($OwnRoots -contains $id) { $own = $true; break }
            $id = $parents[$id]
            if ($null -eq $id) { break }
        }
        if (-not $own) { [pscustomobject]@{ name = $p.Name; id = $p.Id; path = $p.Path } }
    }
}
function Format-Competitors($List) { ($List | ForEach-Object { "$($_.name) ($($_.id))" }) -join ', ' }
foreach ($pair in $Settings) {
    if ($pair -notmatch '^[^=\s"]+=[^"]*$') { throw "Setting '$pair' must be key=value without double quotes" }
}
if (-not [double]::IsFinite($Seconds) -or $Seconds -le 0) { throw 'Seconds must be positive and finite' }
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$cameraPath = (Resolve-Path -LiteralPath $Cameras).Path
$outPath = [System.IO.Path]::GetFullPath($Out)
$contentPath = (Resolve-Path -LiteralPath $Content).Path
$cameraRecord = Get-Content -LiteralPath $cameraPath -Raw | ConvertFrom-Json -AsHashtable
if ($cameraRecord.map -ne $Map -or $cameraRecord.team -ne $Side) { throw 'Camera map/side does not match the selected play session' }
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
$runner = [ordered]@{
    requestedGpuIndex = $GpuIndex; requestedDriver = $Driver; settings = @($Settings)
    quietWaitSeconds = $QuietWaitSeconds; ignoredCompetitors = @($IgnoreCompetitors); waitedSeconds = 0; startedUtc = $null; finishedUtc = $null; exitCode = $null
    contaminated = $false; competitors = @()
}
function Write-Runner { $runner | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outPath 'runner.json') }
# Wait until no other game or renderer runs, so this run neither measures nor disturbs another one.
$waitStart = [DateTime]::UtcNow
while ($true) {
    $busy = @(Get-Competitors @($PID))
    $runner.waitedSeconds = [Math]::Round(([DateTime]::UtcNow - $waitStart).TotalSeconds, 1)
    if ($busy.Count -eq 0) { break }
    if ($runner.waitedSeconds -ge $QuietWaitSeconds) {
        $runner.competitors = $busy
        Write-Runner
        throw "Not started: other game or renderer processes still run after $QuietWaitSeconds s: $(Format-Competitors $busy)"
    }
    if ($runner.waitedSeconds -lt 1) { Write-Host "Waiting up to $QuietWaitSeconds s for $(Format-Competitors $busy) to exit" }
    Start-Sleep -Seconds 1
}
# The exported engine shows its window before managed code runs. Start minimised and unfocused, then the
# benchmark's BackgroundWindow restores it off screen for rendering. Do not change a normal installed build.
$override = Join-Path (Split-Path $exePath) 'override.cfg'
$previousOverride = if (Test-Path -LiteralPath $override) { [IO.File]::ReadAllBytes($override) } else { $null }
$keys = @('SVM_BENCHMARK','SVM_BENCHMARK_OUT','SVM_BENCHMARK_SECONDS','SVM_BENCHMARK_GAMEPLAY','SVM_BENCHMARK_PROFILING','SVM_AUDIO_MUTE')
$previous = @{}
foreach ($key in $keys) { $previous[$key] = [Environment]::GetEnvironmentVariable($key) }
try {
    $overrideText = "[display]`n`nwindow/size/no_focus=true`nwindow/size/mode=1`n"
    foreach ($entry in @($ProjectOverrides | ForEach-Object { $_ -split ',' } | Where-Object { $_ })) {
        $section, $rest = $entry -split '/', 2
        $overrideText += "`n[$section]`n`n$rest`n"
    }
    [IO.File]::WriteAllText($override, $overrideText)
    $env:SVM_BENCHMARK = $cameraPath
    $env:SVM_BENCHMARK_OUT = $outPath
    $env:SVM_BENCHMARK_SECONDS = $Seconds.ToString([Globalization.CultureInfo]::InvariantCulture)
    $env:SVM_BENCHMARK_GAMEPLAY = if ($Gameplay) { '1' } else { '0' }
    $env:SVM_BENCHMARK_PROFILING = if ($Profiling) { '1' } else { '0' }
    $env:SVM_AUDIO_MUTE = '1'
    $arguments = @('--rendering-driver', $Driver)
    if ($null -ne $GpuIndex) { $arguments += @('--gpu-index', "$GpuIndex") }
    $arguments += @($EngineArgs | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
    $arguments += @('--', '--svm-background', "--svm-play=$Map", "--side=$Side", '--rules=playlevel',
        "--bots=$Bots", ('"--svm-content=' + $contentPath + '"'))
    # Double trailing backslashes so the closing quote is not read as an escaped one.
    foreach ($pair in $Settings) { $arguments += ('"--svm-setting=' + ($pair -replace '(\\+)$', '$1$1') + '"') }
    $runner.startedUtc = [DateTime]::UtcNow.ToString('o')
    $process = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $outPath 'game.log') -RedirectStandardError (Join-Path $outPath 'game.err')
    if (-not [FpsBenchmarkProcess]::Unthrottle($process.Handle)) {
        Stop-Process -Id $process.Id -Force
        throw 'Could not disable background power throttling for the benchmark child'
    }
    # Sample once a second while the game runs; any competitor marks the run contaminated.
    $seen = [ordered]@{}
    $deadline = [DateTime]::UtcNow.AddSeconds(600)
    while (-not $process.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) {
            Stop-Process -Id $process.Id -Force
            throw 'Benchmark timed out'
        }
        foreach ($competitor in Get-Competitors @($PID, $process.Id)) {
            if ($seen.Contains("$($competitor.id)")) { continue }
            $competitor | Add-Member -NotePropertyName firstSeenUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
            $seen["$($competitor.id)"] = $competitor
            $runner.contaminated = $true
            $runner.competitors = @($seen.Values)
            Write-Warning "Run contaminated: $(Format-Competitors @($competitor)) started during the benchmark"
        }
    }
    $runner.exitCode = $process.ExitCode
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
    # Restore first so a failed runner.json write cannot leave override.cfg or the variables behind.
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
    if ($null -ne $previousOverride) { [IO.File]::WriteAllBytes($override, $previousOverride) }
    else { Remove-Item -LiteralPath $override -Force -ErrorAction SilentlyContinue }
    $runner.finishedUtc = [DateTime]::UtcNow.ToString('o')
    Write-Runner
    if ($runner.contaminated) {
        Write-Warning "Results are contaminated by $(Format-Competitors $runner.competitors); see $outPath/runner.json"
    }
}
