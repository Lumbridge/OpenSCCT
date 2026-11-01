<#
.SYNOPSIS
    Builds the Godot project, runs the headless smoke tests and writes a JUnit report.

.DESCRIPTION
    Gets the pinned Godot with tools/Get-Godot.ps1 unless -Godot names a console exe, builds
    game/OpenScctGame.slnx, imports the project so a fresh checkout has its .godot cache, then runs each test as
        godot --headless --path game -- --svm-smoke=<name>
    A test passes when Godot exits with 0 and prints "SVM_SMOKE <name> OK". Each name in -ExpectedFailures
    must instead exit with 1 and print "SVM_SMOKE <name> FAIL", which proves the failure path still works.

    With -Export it also exports the "Windows Desktop" preset to local/build, checks the export is complete,
    copies the TestBox fixture to local/build/maps (where a release keeps its maps) and runs the same tests
    with the exported console exe.

    Every step is a test case in the JUnit file (default local/test-results/godot-smoke.xml). The script
    exits with 1 when any step fails.

.PARAMETER Tests
    Smoke tests that must pass, as registered in game/Scripts/Smoke/SmokeTests.cs.

.PARAMETER ExpectedFailures
    Smoke tests that must fail with exit code 1.

.PARAMETER Godot
    Path to a Godot .NET console exe. Defaults to the one tools/Get-Godot.ps1 installs.

.PARAMETER TimeoutSeconds
    Passed to the game as --svm-smoke-timeout. The process is killed 60 s after that.

.PARAMETER Export
    Also export the game and run the tests with the exported exe (downloads the export templates once).

.PARAMETER JUnitPath
    Where to write the JUnit XML report.

.PARAMETER Repeat
    Runs every test this many times and prints how often each one passed, so a flaky test shows up.

.PARAMETER Windowed
    Runs the tests with a window (no --headless) and SVM_AUDIO_MUTE=1, as a player's machine draws them.

.PARAMETER ShotsDir
    With -Windowed, sets SVM_PLAY_SHOTS to <ShotsDir>/<suite>-<test>-<run> for each run (keep it under local/).

.PARAMETER All
    Runs every registered smoke (the list the game prints for an unknown name) except -Exclude and the expected
    failures.

.PARAMETER Exclude
    Tests -All leaves out.

.PARAMETER SkipBuild
    Uses the current build and import (for a second run against the same build).

.PARAMETER Exe
    Runs the tests with this exported console exe (suite "exe") instead of building, importing and running the
    editor: a build made elsewhere, such as a scratch export of one commit. Nothing is written next to it.

.PARAMETER LogDir
    Where each run's stdout and stderr go (default local/test-results/godot-smoke).
#>
[CmdletBinding()]
param(
    [Alias('Test')]
    [string[]]$Tests = @('boot', 'testbox'),
    [string[]]$ExpectedFailures = @('selftest-fail'),
    [string]$Godot,
    [double]$TimeoutSeconds = 120,
    [switch]$Export,
    [string]$JUnitPath,
    [ValidateRange(1, 100)]
    [int]$Repeat = 1,
    [switch]$Windowed,
    [string]$ShotsDir,
    [switch]$All,
    [string[]]$Exclude = @(),
    [switch]$SkipBuild,
    [string]$Exe,
    [string]$LogDir
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gameDir = Join-Path $repoRoot 'game'
$solution = Join-Path $gameDir 'OpenScctGame.slnx'
$buildDir = Join-Path $repoRoot 'local/build'
$exportedConsole = Join-Path $buildDir 'OpenSCCT.console.exe'
$exportedData = Join-Path $buildDir 'data_OpenScctGame_windows_x86_64'
$exportedPck = Join-Path $buildDir 'OpenSCCT.pck'   # the game's pack beside the exe (not embedded: launcher delta updates)
$fixture = Join-Path $repoRoot 'fixtures/testbox'
$logDir = if ($LogDir) { [System.IO.Path]::GetFullPath($LogDir) } else { Join-Path $repoRoot 'local/test-results/godot-smoke' }
if (-not $JUnitPath) { $JUnitPath = Join-Path $repoRoot 'local/test-results/godot-smoke.xml' }
if (-not (Test-Path -LiteralPath $solution)) { throw "No Godot solution at $solution." }
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Map tours and multi-client scenarios have their own budgets. Bot gameplay smokes also include a synchronous
# cold navigation build (both teams on SteeE), so allow startup plus the unchanged per-step gameplay deadlines.
# The cold build is reported by SVM_PLAY load; a warm cache does not consume this allowance.
$minimumTimeouts = @{
    'map-shots' = 1800.0; 'net-lobby' = 360.0; 'net-bots' = 360.0
    'play-shocker' = 240.0; 'play-merc' = 240.0; 'play-combat' = 240.0
}
# Smokes that only run with a window (-All leaves them out of a headless run), and helpers another smoke starts.
$windowOnly = @('golden', 'frame-rate', 'merc-status-visual')
$helpers = @('net-lobby-guest', 'net-bots-guest')
$esc = [char]27
$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param([string]$Suite, [string]$Name, [bool]$Passed, [double]$Seconds, [string]$Message, [string[]]$Output)
    $results.Add([pscustomobject]@{
        Suite = $Suite; Name = $Name; Passed = $Passed; Seconds = $Seconds; Message = $Message; Output = $Output
    })
    $verdict = if ($Passed) { 'PASS' } else { 'FAIL' }
    $detail = if ($Message) { ": $Message" } else { '' }
    Write-Host ("[smoke] {0} {1}/{2} ({3:N1} s){4}" -f $verdict, $Suite, $Name, $Seconds, $detail)
}

function ConvertTo-ArgumentString {
    param([string[]]$Arguments)
    ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join ' '
}

# Runs an exe with a time limit; stdout and stderr go to log files. Returns exit code, output and time.
function Invoke-Logged {
    param([string]$Exe, [string[]]$Arguments, [string]$LogName, [int]$Timeout)
    $outLog = Join-Path $logDir "$LogName.out.log"
    $errLog = Join-Path $logDir "$LogName.err.log"
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Exe -ArgumentList (ConvertTo-ArgumentString $Arguments) -NoNewWindow -PassThru `
        -RedirectStandardOutput $outLog -RedirectStandardError $errLog
    # Reading the handle now keeps ExitCode available after the process exits.
    $null = $process.Handle
    $timedOut = -not $process.WaitForExit($Timeout * 1000)
    if ($timedOut) {
        # The console wrapper starts the real exe as a child, so end the whole tree.
        & taskkill.exe /T /F /PID $process.Id 2>&1 | Out-Null
    }
    $process.WaitForExit()
    $watch.Stop()
    $lines = @()
    foreach ($log in @($outLog, $errLog)) {
        if (Test-Path -LiteralPath $log) {
            $lines += @(Get-Content -LiteralPath $log | ForEach-Object { $_ -replace "$esc\[[0-9;]*m", '' })
        }
    }
    [pscustomobject]@{
        ExitCode = if ($timedOut) { -1 } else { $process.ExitCode }
        TimedOut = $timedOut
        Seconds = $watch.Elapsed.TotalSeconds
        Output = $lines
    }
}

function Invoke-Smoke {
    param([string]$Suite, [string]$Exe, [string[]]$PrefixArgs, [string]$Name, [bool]$ExpectFailure, [int]$Attempt = 1)
    $seconds = [math]::Max($TimeoutSeconds, $(if ($minimumTimeouts.ContainsKey($Name)) { $minimumTimeouts[$Name] } else { 0.0 }))
    $timeoutText = $seconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $processTimeoutSeconds = [int][math]::Ceiling($seconds) + 60
    $arguments = @($PrefixArgs) + @('--', "--svm-smoke=$Name", "--svm-smoke-timeout=$timeoutText")
    $label = if ($Repeat -gt 1) { "$Name#$Attempt" } else { $Name }
    $logName = if ($Repeat -gt 1) { "$Suite-$Name-$Attempt" } else { "$Suite-$Name" }
    $savedShots = $env:SVM_PLAY_SHOTS
    if ($ShotsDir) { $env:SVM_PLAY_SHOTS = Join-Path ([System.IO.Path]::GetFullPath($ShotsDir)) $logName }
    try {
        $run = Invoke-Logged -Exe $Exe -Arguments $arguments -LogName $logName -Timeout $processTimeoutSeconds
    } finally {
        $env:SVM_PLAY_SHOTS = $savedShots
    }
    $verdictLines = @($run.Output | Where-Object { $_.StartsWith("SVM_SMOKE $Name ", [System.StringComparison]::Ordinal) -and
        -not $_.EndsWith(' START', [System.StringComparison]::Ordinal) })
    $verdict = if ($verdictLines.Count -gt 0) { $verdictLines[-1] } else { '' }
    $expectedCode = if ($ExpectFailure) { 1 } else { 0 }
    $expectedLine = if ($ExpectFailure) { "SVM_SMOKE $Name FAIL" } else { "SVM_SMOKE $Name OK" }
    $message = ''
    if ($run.TimedOut) {
        $message = "killed after $processTimeoutSeconds s"
    } elseif ($run.ExitCode -ne $expectedCode) {
        $message = "exit code $($run.ExitCode), expected $expectedCode. $verdict"
    } elseif (-not $verdict.StartsWith($expectedLine, [System.StringComparison]::Ordinal)) {
        $message = "exit code $($run.ExitCode) but no '$expectedLine' line. $verdict"
    }
    $passed = $message.Length -eq 0
    if ($passed) { $message = "exit $($run.ExitCode): $verdict" }
    Add-Result -Suite $Suite -Name $label -Passed $passed -Seconds $run.Seconds -Message $message -Output $run.Output
}

function Invoke-SmokeSet {
    param([string]$Suite, [string]$Exe, [string[]]$PrefixArgs)
    if ($Windowed) { $PrefixArgs = @($PrefixArgs | Where-Object { $_ -ne '--headless' }) }
    $names = @($Tests | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($All) {
        # An unknown name makes the game print every registered smoke and exit with 2.
        $list = Invoke-Logged -Exe $Exe -Arguments (@('--headless') + @($PrefixArgs | Where-Object { $_ -ne '--headless' }) + @('--', '--svm-smoke=?')) `
            -LogName "$Suite-list" -Timeout 120
        $known = @($list.Output | ForEach-Object { if ($_ -match '\(known: ([^)]*)\)') { $Matches[1] -split ',\s*' } })
        if ($known.Count -eq 0) { throw 'The game printed no smoke list.' }
        $names = @($known | Where-Object { $_ -notin $Exclude -and $_ -notin $ExpectedFailures -and $_ -notin $helpers -and
            ($Windowed -or $_ -notin $windowOnly) })
    }
    for ($i = 1; $i -le $Repeat; $i++) {
        foreach ($name in $names) { Invoke-Smoke -Suite $Suite -Exe $Exe -PrefixArgs $PrefixArgs -Name $name -ExpectFailure $false -Attempt $i }
        foreach ($name in $ExpectedFailures) { Invoke-Smoke -Suite $Suite -Exe $Exe -PrefixArgs $PrefixArgs -Name $name -ExpectFailure $true -Attempt $i }
    }
}

# With -Repeat: how often each test passed, flaky ones (some passes, some failures) first.
function Write-FlakeSummary {
    if ($Repeat -le 1) { return }
    $rows = $results | Where-Object { $_.Suite -ne 'setup' } | Group-Object -Property { "$($_.Suite)/$($_.Name -replace '#\d+$', '')" } | ForEach-Object {
        $passes = @($_.Group | Where-Object { $_.Passed }).Count
        [pscustomobject]@{ Test = $_.Name; Passed = $passes; Runs = $_.Count
            Kind = if ($passes -eq $_.Count) { 'stable' } elseif ($passes -eq 0) { 'failing' } else { 'FLAKY' } }
    }
    foreach ($row in ($rows | Sort-Object -Property @{ Expression = { $_.Kind -ne 'FLAKY' } }, Kind, Test)) {
        Write-Host ("[smoke] {0,-8} {1}/{2} {3}" -f $row.Kind, $row.Passed, $row.Runs, $row.Test)
    }
}

function Write-JUnit {
    param([string]$Path)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.CheckCharacters = $false
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    $invariant = [System.Globalization.CultureInfo]::InvariantCulture
    # Control characters other than tab and line breaks are not allowed in XML 1.0.
    $clean = { param([string]$text) [regex]::Replace($text, '[\x00-\x08\x0B\x0C\x0E-\x1F]', '') }
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('testsuites')
        $writer.WriteAttributeString('name', 'godot-smoke')
        $writer.WriteAttributeString('tests', [string]$results.Count)
        $writer.WriteAttributeString('failures', [string]@($results | Where-Object { -not $_.Passed }).Count)
        $total = ($results | Measure-Object -Property Seconds -Sum).Sum
        $writer.WriteAttributeString('time', ([double]$total).ToString('0.000', $invariant))
        foreach ($group in ($results | Group-Object -Property Suite)) {
            $cases = @($group.Group)
            $writer.WriteStartElement('testsuite')
            $writer.WriteAttributeString('name', "godot-smoke.$($group.Name)")
            $writer.WriteAttributeString('tests', [string]$cases.Count)
            $writer.WriteAttributeString('failures', [string]@($cases | Where-Object { -not $_.Passed }).Count)
            $writer.WriteAttributeString('errors', '0')
            $suiteTime = ($cases | Measure-Object -Property Seconds -Sum).Sum
            $writer.WriteAttributeString('time', ([double]$suiteTime).ToString('0.000', $invariant))
            foreach ($case in $cases) {
                $writer.WriteStartElement('testcase')
                $writer.WriteAttributeString('classname', "godot-smoke.$($case.Suite)")
                $writer.WriteAttributeString('name', $case.Name)
                $writer.WriteAttributeString('time', ([double]$case.Seconds).ToString('0.000', $invariant))
                if (-not $case.Passed) {
                    $writer.WriteStartElement('failure')
                    $writer.WriteAttributeString('message', (& $clean $case.Message))
                    $writer.WriteString((& $clean (@($case.Output | Select-Object -Last 60) -join "`n")))
                    $writer.WriteEndElement()
                }
                if ($case.Output) {
                    $writer.WriteElementString('system-out', (& $clean (@($case.Output) -join "`n")))
                }
                $writer.WriteEndElement()
            }
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    } finally {
        $writer.Dispose()
    }
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Body)
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $output = @(& $Body)
        Add-Result -Suite 'setup' -Name $Name -Passed $true -Seconds $watch.Elapsed.TotalSeconds -Message '' -Output $output
        return $true
    } catch {
        Add-Result -Suite 'setup' -Name $Name -Passed $false -Seconds $watch.Elapsed.TotalSeconds -Message "$_" -Output @("$_")
        return $false
    }
}

if ($Exe) {
    $exePath = [System.IO.Path]::GetFullPath($Exe)
    if (-not (Test-Path -LiteralPath $exePath)) { throw "No exe at $exePath." }
    if ($Windowed) { $env:SVM_AUDIO_MUTE = '1' }
    Invoke-SmokeSet -Suite 'exe' -Exe $exePath -PrefixArgs @('--headless')
    Write-JUnit -Path $JUnitPath
    Write-FlakeSummary
    $failed = @($results | Where-Object { -not $_.Passed })
    Write-Host "[smoke] $($results.Count - $failed.Count) of $($results.Count) passed; report: $JUnitPath"
    if ($failed.Count -gt 0) { exit 1 }
    exit 0
}

$ok = Invoke-Step 'get-godot' {
    if (-not $script:Godot) {
        $script:Godot = if ($Export) {
            & (Join-Path $repoRoot 'tools/Get-Godot.ps1') -ExportTemplates | Select-Object -Last 1
        } else {
            & (Join-Path $repoRoot 'tools/Get-Godot.ps1') | Select-Object -Last 1
        }
    }
    if (-not (Test-Path -LiteralPath $script:Godot)) { throw "Godot not found at '$script:Godot'." }
    "godot: $script:Godot"
}

if ($Windowed) { $env:SVM_AUDIO_MUTE = '1' }

if ($ok -and -not $SkipBuild) {
    $ok = Invoke-Step 'build' {
        $run = Invoke-Logged -Exe 'dotnet' -Arguments @('build', $solution, '-c', 'Debug', '--nologo', '-v', 'q') -LogName 'build' -Timeout 600
        if ($run.ExitCode -ne 0) { throw "dotnet build failed with exit code $($run.ExitCode): $(($run.Output | Select-Object -Last 20) -join ' | ')" }
        $run.Output
    }
}

if ($ok -and -not $SkipBuild) {
    $ok = Invoke-Step 'import' {
        $run = Invoke-Logged -Exe $script:Godot -Arguments @('--headless', '--path', $gameDir, '--import') -LogName 'import' -Timeout 600
        if ($run.ExitCode -ne 0) { throw "Godot import failed with exit code $($run.ExitCode)." }
        $run.Output
    }
}

if ($ok) {
    Invoke-SmokeSet -Suite 'editor' -Exe $script:Godot -PrefixArgs @('--headless', '--path', $gameDir)
}

if ($ok -and $Export) {
    $exported = Invoke-Step 'export' {
        # The export exits with 0 even when it fails, so start clean and check what it wrote.
        foreach ($stale in @((Join-Path $buildDir 'OpenSCCT.exe'), $exportedConsole, $exportedData, $exportedPck)) {
            if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
        }
        New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
        $target = Join-Path $buildDir 'OpenSCCT.exe'
        $run = Invoke-Logged -Exe $script:Godot -Arguments @('--headless', '--path', $gameDir, '--export-release', 'Windows Desktop', $target) `
            -LogName 'export' -Timeout 900
        if ($run.ExitCode -ne 0) { throw "Godot export failed with exit code $($run.ExitCode)." }
        foreach ($needed in @($exportedConsole, $exportedData, $exportedPck)) {
            if (-not (Test-Path -LiteralPath $needed)) { throw "Export incomplete: $needed is missing." }
        }
        foreach ($dll in @('OpenScctGame.dll', 'OpenScct.Core.dll', 'OpenScct.Content.dll', 'OpenScct.Net.dll', 'OpenScct.Server.dll', 'LiteNetLib.dll', 'DiscordRPC.dll', 'Newtonsoft.Json.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $exportedData $dll))) { throw "Export incomplete: $dll is missing." }
        }

        # This scratch build is for test runs: its window starts minimised and never takes focus (override.cfg next to the exe).
        Set-Content -LiteralPath (Join-Path $buildDir 'override.cfg') -Value "[display]`n`nwindow/size/no_focus=true`nwindow/size/mode=1"

        # Releases keep their maps in maps/ next to the exe.
        $maps = Join-Path $buildDir 'maps'
        $mapTarget = Join-Path $maps 'testbox'
        if (Test-Path -LiteralPath $mapTarget) { Remove-Item -LiteralPath $mapTarget -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $maps | Out-Null
        Copy-Item -LiteralPath $fixture -Destination $mapTarget -Recurse

        # The TestRange station manifest, where SmokePaths.Tracked finds it when the build is not inside a checkout.
        $stations = Join-Path $repoRoot 'fixtures/testrange/stations.json'
        if (Test-Path -LiteralPath $stations) {
            $shipped = Join-Path $buildDir 'fixtures/testrange'
            New-Item -ItemType Directory -Force -Path $shipped | Out-Null
            Copy-Item -LiteralPath $stations -Destination $shipped -Force
        }
        $run.Output
    }
    if ($exported) {
        Invoke-SmokeSet -Suite 'export' -Exe $exportedConsole -PrefixArgs @('--headless')
    } else {
        $ok = $false
    }
}

Write-JUnit -Path $JUnitPath
Write-FlakeSummary
$failed = @($results | Where-Object { -not $_.Passed })
Write-Host "[smoke] $($results.Count - $failed.Count) of $($results.Count) passed; report: $JUnitPath"
if ($failed.Count -gt 0) { exit 1 }
exit 0
