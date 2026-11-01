<#
.SYNOPSIS
Starts, drives, inspects and stops two isolated real game processes over local UDP.
.EXAMPLE
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario stall -Windowed
.EXAMPLE
pwsh tools/network-lab.ps1 -Action Start -Map TestBox -Windowed
#>
[CmdletBinding()]
param(
    [ValidateSet('Start','Run','Send','Status','Stop')][string]$Action = 'Start',
    [string]$RunDirectory,
    [string]$Map = 'TestBox',
    [switch]$Hub,
    [switch]$Dedicated,
    [switch]$Observer,
    [switch]$SecondObserver,
    [switch]$AirborneDeath,
    [ValidateSet('standing','crouched')][string]$DeathStance = 'standing',
    [ValidateSet('knockout','neck-break','timeout')][string]$GrabOutcome = 'knockout',
    [ValidateSet('standing','crouched')][string]$TargetStance = 'standing',
    [ValidateRange(0,2)][double]$FireSeconds = 0,
    [ValidateRange(0,3500)][int]$GadgetDownBlackoutMs = 0,
    [ValidateRange(0,1)][int]$ObserverTeam = 0,
    [ValidateRange(-1,1)][int]$SecondObserverTeam = -1,
    [ValidateRange(-1,10)][int]$Lives = -1,
    [ValidateRange(-1,15)][int]$RespawnSeconds = -1,
    [ValidateSet('host','guest')][string]$VictimRole = 'host',
    [ValidateSet('none','first','last')][string]$SpectatorDisconnect = 'none',
    [switch]$ReverseRoles,
    [switch]$Windowed,
    [switch]$SkipBuild,
    [ValidateRange(0,75000)][int]$LoadDelayMs = 5000,
    [ValidateSet('direct','lan','typical','rough','asymmetric','limited')][string]$NetworkProfile = 'direct',
    [int]$Seed = 119,
    [ValidateRange(15,360)][int]$HostFps = 60,
    [ValidateRange(15,360)][int]$GuestFps = 60,
    [ValidateRange(60,7200)][int]$SoakSeconds = 1800,
    [string]$Godot,
    [string]$GameExe,
    [string]$DedicatedExe,
    [ValidateSet('host','guest','observer','observer2')][string]$Role = 'host',
    [string]$CommandJson,
    [ValidateSet('startup','load-timeout','jump','ledge','stall','grab','fire','gadget','outage','stance','soak','death','elimination','roundtrip','rejoin','spectator','stale-combat')][string]$Scenario = 'startup',
    [ValidateRange(30,900)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
$repo = Split-Path -Parent $PSScriptRoot
if ($Dedicated -and $Hub) { throw 'Dedicated lab mode currently exercises regular sessions; use -Hub separately.' }
if ($Scenario -eq 'load-timeout' -and ($Action -ne 'Run' -or $Hub -or $LoadDelayMs -lt 65000)) {
    throw 'Load-timeout requires -Action Run, a regular session and -LoadDelayMs of at least 65000.'
}
if ($Hub) { $Map = 'Hub' } # HubService always hosts HubK.MapName; record the map actually loaded.
$dedicatedProject = Join-Path $repo 'tools/network-lab-server/OpenScct.NetworkLab.Server.csproj'
if ($DedicatedExe) {
    if (!$SkipBuild) { throw 'An explicit -DedicatedExe requires -SkipBuild to preserve its build identity.' }
    $dedicatedExe = (Resolve-Path -LiteralPath $DedicatedExe).Path
} else {
    $dedicatedExe = Join-Path $repo 'tools/network-lab-server/bin/Debug/net10.0/OpenScct.NetworkLab.Server.exe'
}
$roles = @('host','guest')
if ($Observer -or $SecondObserver) { $roles += 'observer' }
if ($SecondObserver) { $roles += 'observer2' }
function Read-Json([string]$path) {
    $stream = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    $reader = [IO.StreamReader]::new($stream)
    try { $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
}
function Write-Json([string]$path, $value) {
    $value | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath ($path + '.tmp') -Encoding utf8
    Move-Item -LiteralPath ($path + '.tmp') -Destination $path -Force
}
function Send-Lab([string]$roleName, $command) {
    $id = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfffffff') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
    Write-Json (Join-Path $RunDirectory "$roleName/$id.json") $command
    return $id
}
function Wait-Ack([string]$roleName, [string]$id) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $path = Join-Path $RunDirectory "$roleName-acks/$id.json"
    while (!(Test-Path -LiteralPath $path)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "No acknowledgement for $roleName command $id" }
        $manifest = Read-Json (Join-Path $RunDirectory 'run.json')
        $owned = $manifest.processes | Where-Object role -eq $roleName
        $process = Get-Process -Id $owned.id -ErrorAction SilentlyContinue
        if (!$process -or $process.StartTime.ToUniversalTime().Ticks.ToString() -ne $owned.startTicks) {
            throw "$roleName exited while waiting for command $id. See its log."
        }
        Start-Sleep -Milliseconds 200
    }
    $ack = Read-Json $path
    if (!$ack.ok) { throw $ack.error }
}
function Own-Pawn($status) { @($status.pawns | Where-Object role -eq 'ROLE_AutonomousProxy') | Select-Object -First 1 }
function Wait-Respawn([string]$roleName, $previousPawn) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    do {
        $state = Read-Json (Join-Path $RunDirectory "$roleName-status.json")
        $pc = $state.localController
        if ($state.error) { throw "$roleName failed while waiting for respawn: $($state.error)" }
        if ($pc.pawn -and $pc.pawn -ne $previousPawn -and $pc.health -gt 0 -and !$pc.spectating -and
            $pc.state -notmatch 'Dead|Spectat|Waiting') {
            $freshPawn = $pc.pawn
            Invoke-Lab $roleName @{op='input';steps=@(@{label='fresh respawn stays playable';seconds=2})}
            $stable = Read-Json (Join-Path $RunDirectory "$roleName-status.json")
            $pc = $stable.localController
            if ($pc.pawn -ne $freshPawn -or $pc.health -le 0 -or $pc.spectating -or $pc.state -match 'Dead|Spectat|Waiting' -or $pc.view -ne $pc.pawn) {
                throw "$roleName lost its fresh playable possession after respawn."
            }
            return $stable
        }
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw "$roleName did not finish fresh possession after its respawn deadline." }
        Start-Sleep -Milliseconds 100
    } while ($true)
}
function Assert-NoHealthyDeath([string]$playerName, [DateTimeOffset]$since) {
    foreach ($sample in @(Trace-Since 'server' $since)) {
        $pc = @($sample.server.players | Where-Object name -eq $playerName)[0].controller
        if ($pc.health -gt 0 -and $pc.state -match '^s_Dead$|s_MultiSpectating_WhenKilled') {
            throw 'Authority put a healthy respawn back into death/spectating.'
        }
    }
}
function Invoke-Lab([string]$roleName, $command) { $id = Send-Lab $roleName $command; Wait-Ack $roleName $id }
function Trace-Since([string]$roleName, [DateTimeOffset]$since) {
    $stream = [IO.FileStream]::new((Join-Path $RunDirectory "$roleName-trace.jsonl"), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    $reader = [IO.StreamReader]::new($stream)
    try {
        $snapshot = $reader.ReadToEnd()
        # A concurrent append may leave the last line incomplete in this snapshot.
        $end = $snapshot.LastIndexOf("`n")
        if ($end -lt 0) { return }
        foreach ($line in $snapshot.Substring(0,$end).Split("`n")) {
            if (!$line.Trim()) { continue }
            $sample = $line | ConvertFrom-Json
            if ([DateTimeOffset]$sample.utc -ge $since) { $sample }
        }
    } finally { $reader.Dispose() }
}
function Assert-Startup {
    if ($LoadDelayMs -lt 1000) { return 'NOT RUN: delayed-load barrier needs at least one second of injected load delay.' }
    $loading = Read-Json (Join-Path $RunDirectory 'guest-loading.json')
    $from = ([DateTimeOffset]$loading.started).AddMilliseconds(250)
    $until = ([DateTimeOffset]$loading.started).AddMilliseconds($loading.milliseconds - 200)
    $samples = @(Trace-Since 'server' $from | Where-Object { [DateTimeOffset]$_.utc -lt $until -and $null -ne $_.server })
    if ($samples.Count -lt 3) { throw 'Insufficient live server samples to prove the delayed-load barrier.' }
    foreach ($sample in $samples) {
        if ($Hub) {
            if (@($sample.server.pawns | Where-Object name -eq 'LabGuest').Count -gt 0) { throw 'Hub spawned the guest before its scene was ready.' }
        } elseif ($sample.server.started -or @($sample.server.pawns).Count -gt 0) {
            throw 'Regular match started or spawned a player while the guest was loading.'
        }
    }
    return "PASS: $($samples.Count) server samples during delayed guest load."
}
function Wait-SpectatorTarget([string]$roleName, [string[]]$names) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $status = Read-Json (Join-Path $RunDirectory "$roleName-status.json")
        if ($status.error) { throw "$roleName failed: $($status.error)" }
        if ($status.localController.spectating -and $status.localController.target -in $names) {
            if (!$Windowed -or ($status.hud.page -eq 'SpectatorHudPage' -and $status.hud.Watched -in $names -and $status.hud.NoRespawn -and $status.hud.Lives -eq '0' -and $status.hud.TeamLives -eq $names.Count.ToString())) { return $status }
        }
        if ([DateTime]::UtcNow -gt $deadline) { throw "$roleName never spectated a permitted living teammate: $($names -join ', ')." }
        Start-Sleep -Milliseconds 100
    } while ($true)
}
function Assert-LoadTimeout {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $loadingPath = Join-Path $RunDirectory 'guest-loading.json'
    while (!(Test-Path -LiteralPath $loadingPath)) {
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'Guest never reached its loading stall.' }
        Start-Sleep -Milliseconds 200
    }
    $loading = Read-Json $loadingPath
    $from = [DateTimeOffset]$loading.started
    # Wait beyond the late scene-ready acknowledgement, not just the server's removal deadline.
    while ([DateTimeOffset]::UtcNow -lt $from.AddMilliseconds($loading.milliseconds).AddSeconds(25)) {
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'Load-timeout observation exceeded its deadline.' }
        Start-Sleep -Milliseconds 200
    }
    $early = @(Trace-Since 'server' $from.AddSeconds(1) | Where-Object { [DateTimeOffset]$_.utc -le $from.AddSeconds(45) })
    if ($early.Count -lt 20) { throw 'Insufficient authority evidence during the long loading stall.' }
    foreach ($sample in $early) {
        if ($sample.server.started -or @($sample.server.pawns).Count) { throw 'Match started before the loading participant timed out.' }
    }
    $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
    $guest = Read-Json (Join-Path $RunDirectory 'guest-status.json')
    $removed = @($source.disconnects | Where-Object { $_.player -eq 'LabGuest' -and $_.reason -match 'timed out|timeout' })
    if (!$removed.Count -or @($source.players | Where-Object name -eq 'LabGuest').Count -or
        @($source.pawns | Where-Object name -eq 'LabGuest').Count) { throw 'Timed-out loader retained a server connection or pawn.' }
    if (!$source.started -or !@($source.players | Where-Object { $_.name -eq 'LabHost' -and $_.ready }).Count) {
        throw 'The ready host did not proceed after removing the failed loader.'
    }
    $guestError = if ($guest.error) { $guest.error } else { $guest.onlineError }
    while (!$guestError -and !(Own-Pawn $guest)) {
        # The injected stall ends before rendering/content load finishes. The ordinary idle timeout starts
        # after that actual hitch and buffered traffic, not exactly 25 seconds after the injected sleep.
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw "Late loader never exposed its connection error (phase $($guest.phase))." }
        Start-Sleep -Milliseconds 200
        $guest = Read-Json (Join-Path $RunDirectory 'guest-status.json')
        $guestError = if ($guest.error) { $guest.error } else { $guest.onlineError }
    }
    if ((Own-Pawn $guest) -or !$guestError) { throw 'Late loader did not expose a connection error without a playable pawn.' }
    Write-Json (Join-Path $RunDirectory 'load-timeout-result.json') @{loading=$loading;earlySamples=$early.Count;disconnects=$removed;source=$source;guestPhase=$guest.phase;guestError=$guestError;observedAfterStallSeconds=([DateTimeOffset]::UtcNow-$from.AddMilliseconds($loading.milliseconds)).TotalSeconds}
    return "PASS: $($early.Count) authority samples before timeout; late readiness did not restore the removed player."
}
function Wait-Playing {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        foreach ($owned in $ownedProcesses) {
            if (!(Get-Process -Id $owned.id -ErrorAction SilentlyContinue)) { throw "$($owned.role) exited. See $RunDirectory logs." }
        }
        $ready = $true
        foreach ($roleName in $roles) {
            $path = Join-Path $RunDirectory "$roleName-status.json"
            if (!(Test-Path -LiteralPath $path)) { $ready = $false; continue }
            $status = Read-Json $path
            if ($status.error) { throw "$roleName failed: $($status.error)" }
            if (!(Own-Pawn $status) -or !$status.pilotReady) { $ready = $false }
            elseif ($roleName -eq 'observer2' -and $SecondObserverTeam -ge 0 -and $status.localController.team -ne $SecondObserverTeam) {
                throw 'Second observer joined the wrong team; use a build supporting observer2Team.'
            }
        }
        if ($ready) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Players did not become playable. See $RunDirectory logs and status files."
}
function Stop-Lab {
    $manifest = Read-Json (Join-Path $RunDirectory 'run.json')
    foreach ($owned in $manifest.processes | Where-Object role -ne 'proxy') { $null = Send-Lab $owned.role @{op='stop'} }
    if (@($manifest.processes | Where-Object role -eq 'proxy').Count -gt 0) {
        Write-Json (Join-Path $RunDirectory 'proxy-control.json') @{id=[guid]::NewGuid().ToString('N');stop=$true}
    }
    Start-Sleep -Seconds 2
    foreach ($owned in $manifest.processes) {
        $process = Get-Process -Id $owned.id -ErrorAction SilentlyContinue
        # A reused PID must never terminate a different game or the user's application.
        if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $owned.startTicks) {
            if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id }
        }
    }
}
if ($Action -in @('Start','Run')) {
    if (!$RunDirectory) { $RunDirectory = Join-Path $repo ('local/network-lab/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
    $RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
    if (Test-Path -LiteralPath $RunDirectory) { throw 'Use a new run directory so results and process ownership cannot be overwritten.' }
    $null = New-Item -ItemType Directory -Path $RunDirectory
    if (!$SkipBuild -and !$GameExe) {
        & dotnet build (Join-Path $repo 'game/OpenScctGame.slnx') --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Game build failed.' }
        if ($Dedicated) {
            & dotnet build $dedicatedProject --verbosity quiet
            if ($LASTEXITCODE -ne 0) { throw 'Dedicated lab server build failed.' }
        }
    }
    if ($GameExe) {
        if ($Godot) { throw 'Choose -GameExe for an exported build or -Godot for the project.' }
        $Godot = (Resolve-Path -LiteralPath $GameExe).Path
        if ($Windowed -and !(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $Godot) 'override.cfg'))) {
            throw 'Copy game/override.cfg beside the exported executable before running windowed tests.'
        }
    } elseif (!$Godot) { $Godot = (& (Join-Path $PSScriptRoot 'Get-Godot.ps1') | Select-Object -Last 1) }
    $Godot = (Resolve-Path -LiteralPath $Godot).Path
    $socket = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback,0))
    $port = $socket.Client.LocalEndPoint.Port
    $socket.Dispose()
    $ownedProcesses = @()
    $processLifetime = if ($Scenario -eq 'soak') { $SoakSeconds + 300 } else { 900 }
    try {
        $joinPort = $port
        if ($NetworkProfile -ne 'direct') {
            $proxyStart = [Diagnostics.ProcessStartInfo]::new((Get-Command python).Source)
            $proxyStart.UseShellExecute = $false
            $proxyStart.CreateNoWindow = $true
            $proxyStart.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            foreach ($argument in @((Join-Path $PSScriptRoot 'network-lab-proxy.py'),'--run-directory',$RunDirectory,'--server-port',"$port",'--profile',$NetworkProfile,'--seed',"$Seed",'--seconds',"$processLifetime")) { $proxyStart.ArgumentList.Add($argument) }
            $proxy = [Diagnostics.Process]::Start($proxyStart)
            $ownedProcesses += @{role='proxy';id=$proxy.Id;startTicks=$proxy.StartTime.ToUniversalTime().Ticks.ToString()}
            $proxyReady = Join-Path $RunDirectory 'proxy-ready.json'
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            while (!(Test-Path -LiteralPath $proxyReady)) {
                if ($proxy.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'UDP proxy did not start.' }
                Start-Sleep -Milliseconds 100
            }
            $joinPort = (Read-Json $proxyReady).port
        }
        $contentRoot = Join-Path $repo '.content'
        Write-Json (Join-Path $RunDirectory 'config.json') @{map=$Map;contentRoot=$contentRoot; hub=[bool]$Hub; dedicated=[bool]$Dedicated;port=$port; joinPort=$joinPort; loadDelayMs=$LoadDelayMs;players=$roles.Count;hostTeam=$(if($ReverseRoles){1}else{0});observerTeam=$ObserverTeam;observer2Team=$(if($SecondObserverTeam -ge 0){$SecondObserverTeam}else{$ObserverTeam});lives=$Lives;respawnSeconds=$RespawnSeconds;sampleIntervalMs=$(if($Scenario -eq 'soak'){1000}else{100})}
        if ($Dedicated) {
            if (!(Test-Path -LiteralPath $dedicatedExe)) { throw 'Build the dedicated lab server before using -SkipBuild.' }
            $serverArgs = @('"' + $RunDirectory + '"', "$processLifetime")
            $serverProcess = Start-Process -FilePath $dedicatedExe -ArgumentList $serverArgs -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $RunDirectory 'dedicated.log') -RedirectStandardError (Join-Path $RunDirectory 'dedicated-error.log')
            $ownedProcesses += @{role='server';id=$serverProcess.Id;startTicks=$serverProcess.StartTime.ToUniversalTime().Ticks.ToString()}
            $deadline = [DateTime]::UtcNow.AddSeconds(30)
            while (!(Test-Path -LiteralPath (Join-Path $RunDirectory 'listening.json'))) {
                if ($serverProcess.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Dedicated lab server failed to start; see dedicated-error.log.' }
                Start-Sleep -Milliseconds 100
            }
        }
        foreach ($roleName in $roles) {
            $null = New-Item -ItemType Directory -Force -Path (Join-Path $RunDirectory $roleName)
            $start = [Diagnostics.ProcessStartInfo]::new($Godot)
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            $start.WorkingDirectory = $repo
            $start.Environment['SVM_NETLAB_DIR'] = $RunDirectory
            $start.Environment['SVM_CONTENT'] = $contentRoot
            $start.Environment['SVM_AUDIO_MUTE'] = '1'
            $start.Environment['SVM_BACKGROUND'] = '1'
            $start.Environment['SVM_PLAY_SHOTS'] = Join-Path $RunDirectory 'shots'
            $fps = if ($roleName -eq 'host') { $HostFps } else { $GuestFps }
            $arguments = @('--log-file', (Join-Path $RunDirectory "$roleName.log"), '--max-fps',"$fps")
            if (!$GameExe) { $arguments = @('--path', (Join-Path $repo 'game')) + $arguments }
            if (!$Windowed) { $arguments += '--headless' }
            else { $arguments += @('--resolution','960x540','--position', $(if ($roleName -eq 'host') {'40,80'} else {'1020,80'})) }
            $arguments += @('--', "--svm-smoke=network-lab-$roleName", "--svm-smoke-timeout=$processLifetime")
            foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
            $process = [Diagnostics.Process]::Start($start)
            $ownedProcesses += @{role=$roleName; id=$process.Id; startTicks=$process.StartTime.ToUniversalTime().Ticks.ToString()}
        }
        $fingerprints = @()
        $dirty = @(& git -C $repo ls-files --modified --others --exclude-standard)
        foreach ($relative in $dirty) {
            $path = Join-Path $repo $relative
            if (Test-Path -LiteralPath $path -PathType Leaf) { $fingerprints += @{path=$relative;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash} }
        }
        $assemblyDirectory = if ($GameExe) { Join-Path (Split-Path -Parent $Godot) 'data_OpenScctGame_windows_x86_64' } else { Join-Path $repo 'game/.godot/mono/temp/bin/Debug' }
        foreach ($file in Get-ChildItem -LiteralPath $assemblyDirectory -Filter 'OpenScct*.dll') {
            $fingerprints += @{path=$file.FullName;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
        }
        if ($GameExe) {
            foreach ($file in @($Godot, (Join-Path (Split-Path -Parent $Godot) 'OpenSCCT.pck'))) {
                $fingerprints += @{path=$file;sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash}
            }
        }
        if ($Dedicated) {
            foreach ($file in Get-ChildItem -LiteralPath (Split-Path -Parent $dedicatedExe) -Filter 'OpenScct*.dll') {
                $fingerprints += @{path=$file.FullName;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
            }
        }
        $contentMap = if ($Hub) { 'Hub' } else { $Map }
        foreach ($relative in @('manifest.json','sim/gameplay.json','sim/collision.bin','render/world.glb')) {
            $path = Join-Path $repo ".content/svmap/$contentMap/$relative"
            if (Test-Path -LiteralPath $path) { $fingerprints += @{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash} }
        }
        foreach ($relative in @('PRO.anim.json','Def.anim.json','ATT_01.glb','DEF_01.glb')) {
            $path = Join-Path $repo ".content/shared/chars/SPerso/$relative"
            if (Test-Path -LiteralPath $path) { $fingerprints += @{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash} }
        }
        Write-Json (Join-Path $RunDirectory 'run.json') @{directory=$RunDirectory; executable=$Godot; exported=[bool]$GameExe; map=$Map; hub=[bool]$Hub; dedicated=[bool]$Dedicated;rendered=[bool]$Windowed; port=$port; joinPort=$joinPort; networkProfile=$NetworkProfile; seed=$Seed; hostFps=$HostFps; guestFps=$GuestFps; scenario=$Scenario; scenarioParameters=@{lives=$Lives;respawnSeconds=$RespawnSeconds;victimRole=$VictimRole;deathStance=$DeathStance;grabOutcome=$GrabOutcome;targetStance=$TargetStance;fireSeconds=$FireSeconds;gadgetDownBlackoutMs=$GadgetDownBlackoutMs;airborneDeath=[bool]$AirborneDeath;spectatorDisconnect=$SpectatorDisconnect;observerTeam=$ObserverTeam;secondObserverTeam=$SecondObserverTeam;reverseRoles=[bool]$ReverseRoles;soakSeconds=$SoakSeconds;loadDelayMs=$LoadDelayMs}; processes=$ownedProcesses; commit=(& git -C $repo rev-parse HEAD); dirty=$dirty; fingerprints=$fingerprints; started=[DateTimeOffset]::UtcNow}
        if ($Scenario -ne 'load-timeout') { Wait-Playing }
    } catch {
        if (Test-Path -LiteralPath (Join-Path $RunDirectory 'run.json')) { Stop-Lab }
        else {
            foreach ($owned in $ownedProcesses) {
                $process = Get-Process -Id $owned.id -ErrorAction SilentlyContinue
                if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $owned.startTicks) { Stop-Process -Id $process.Id }
            }
        }
        throw
    }
    Write-Output "Network lab ready: $RunDirectory"
    if ($Action -eq 'Start') { return }
} else {
    if (!$RunDirectory) { throw 'Specify -RunDirectory from the Start output.' }
    $RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
}
switch ($Action) {
    Send { $id = Send-Lab $Role ($CommandJson | ConvertFrom-Json); Wait-Ack $Role $id; Write-Output "Completed $Role/$id" }
    Status { foreach ($roleName in @('host','guest')) { Read-Json (Join-Path $RunDirectory "$roleName-status.json") } }
    Stop { Stop-Lab }
    Run {
        $passed = $false
        $failure = $null
        $startup = 'NOT RUN'
        try {
            if ($Scenario -eq 'load-timeout') {
                $startup = Assert-LoadTimeout
                $passed = $true
                Write-Output "PASS $Scenario : $RunDirectory"
                return
            }
            $startup = Assert-Startup
            if ($Scenario -eq 'gadget') {
                if ($Hub -or $ReverseRoles) { throw 'The grenade fixture currently needs a regular-session remote merc.' }
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='settle before grenade';seconds=10})}
                $thrower = Own-Pawn (Read-Json (Join-Path $RunDirectory 'guest-status.json'))
                $viewer = Own-Pawn (Read-Json (Join-Path $RunDirectory 'host-status.json'))
                if (!$thrower.gadget -or $thrower.gadget.type -notlike '*Gren*') { throw 'Expected the normal merc grenade loadout.' }
                $candidates = @()
                foreach ($degrees in @(180,135,225,90,270,0,45,315)) {
                    $angle = ($thrower.yaw * 2 * [Math]::PI / 65536) + $degrees * [Math]::PI / 180
                    $candidates += ,@(($thrower.position[0]+260*[Math]::Cos($angle)),($thrower.position[1]+260*[Math]::Sin($angle)),($thrower.position[2]+$viewer.height-$thrower.height))
                }
                Invoke-Lab 'host' @{op='place';player='LabHost';positions=$candidates;yaw=0;requireSupport=$true}
                Invoke-Lab 'host' @{op='input';steps=@(@{label='grenade observer settles';seconds=2})}
                $viewer = Own-Pawn (Read-Json (Join-Path $RunDirectory 'host-status.json'))
                $yaw = [int]([Math]::Atan2($thrower.position[1]-$viewer.position[1],$thrower.position[0]-$viewer.position[0])*65536/(2*[Math]::PI))
                Invoke-Lab 'host' @{op='face';yaw=$yaw;pitch=-2000}
                $blackoutEnd = $null
                if ($GadgetDownBlackoutMs -gt 0) {
                    if ($NetworkProfile -eq 'direct') { throw 'Grenade blackout requires the UDP proxy.' }
                    $control = [guid]::NewGuid().ToString('N')
                    Write-Json (Join-Path $RunDirectory 'proxy-control.json') @{id=$control;downBlackoutMs=$GadgetDownBlackoutMs}
                    $deadline = [DateTime]::UtcNow.AddSeconds(5)
                    do {
                        $proxy = Read-Json (Join-Path $RunDirectory 'proxy-status.json')
                        if ($proxy.appliedControl -eq $control -and $proxy.downBlackout) {
                            $blackoutEnd = [DateTimeOffset]::FromUnixTimeMilliseconds($proxy.downBlackoutEndMs)
                            break
                        }
                        if ([DateTime]::UtcNow -gt $deadline) { throw 'Proxy did not apply the grenade downlink blackout.' }
                        Start-Sleep -Milliseconds 100
                    } while ($true)
                }
                $since = [DateTimeOffset]::UtcNow
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='charge grenade';seconds=0.4;hold=@('bAltFire')},@{label='release and observe fuse';seconds=8})}
                $serverTrace = @(Trace-Since 'server' $since)
                $projectiles = @($serverTrace | ForEach-Object { $_.server.grenades } | Where-Object owner -eq $thrower.name)
                $guids = @($projectiles | Select-Object -ExpandProperty netGuid -Unique)
                if ($guids.Count -ne 1) { throw "Expected exactly one authoritative grenade, saw $($guids.Count)." }
                $seen = @{}
                foreach ($roleName in @('host','guest')) {
                    # This controlled interval contains exactly one throw; compare type/owner and one local lifetime.
                    # Local actor IDs differ between worlds; the persistent channel also retains the server GUID.
                    $observed = @(Trace-Since $roleName $since | ForEach-Object { $_.grenades } | Where-Object { $_.owner -eq $thrower.name -and $_.type -eq $projectiles[0].type })
                    $localIds = @($observed | Select-Object -ExpandProperty id -Unique)
                    $seen[$roleName] = @{samples=$observed.Count;localIds=$localIds}
                    if ($GadgetDownBlackoutMs -ge 3000 -and $roleName -eq 'guest') {
                        # The whole authoritative lifetime must have elapsed during the outage. An old reliable
                        # opening may be processed alongside its close, but must not leave a visible ghost.
                        $authorityAfter = @($serverTrace | Where-Object { [DateTimeOffset]$_.utc -ge $blackoutEnd } | ForEach-Object { $_.server.grenades })
                        if ($authorityAfter.Count -ne 0) { throw 'Grenade lived beyond the requested full-lifetime blackout; fixture not established.' }
                        $lateCopies = @(Trace-Since $roleName ($blackoutEnd.AddSeconds(0.5)) | ForEach-Object { $_.grenades })
                        if ($lateCopies.Count -ne 0 -or $localIds.Count -gt 1) { throw 'A stale grenade survived recovery after its authoritative fuse.' }
                    } elseif ($observed.Count -lt 2 -or $localIds.Count -ne 1) { throw "$roleName did not observe exactly one grenade lifetime." }
                    if (@((Read-Json (Join-Path $RunDirectory "$roleName-status.json")).grenades).Count -ne 0) { throw "$roleName retained a grenade after its fuse." }
                }
                $owner = Own-Pawn (Read-Json (Join-Path $RunDirectory 'guest-status.json'))
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object name -eq $thrower.name
                Write-Json (Join-Path $RunDirectory 'gadget-result.json') @{before=$thrower.gadget;owner=$owner.gadget;source=$source.gadget;projectiles=$guids;observedSamples=$seen;downBlackoutMs=$GadgetDownBlackoutMs;blackoutEnd=$blackoutEnd}
                if ($source.gadget.charge -ne ($thrower.gadget.charge-1) -or $owner.gadget.charge -ne $source.gadget.charge) { throw 'Grenade charge did not decrease exactly once on owner and authority.' }
            }
            if ($Scenario -eq 'stale-combat') {
                if ($Hub -or $NetworkProfile -ne 'lan' -or $Lives -lt 2 -or $RespawnSeconds -ne 1) {
                    throw 'Stale-combat needs a regular session, -NetworkProfile lan, -Lives 2 or more and -RespawnSeconds 1.'
                }
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='settle before delayed combat';seconds=10})}
                $before = (Read-Json (Join-Path $RunDirectory 'guest-status.json')).localController
                $control = [guid]::NewGuid().ToString('N')
                Write-Json (Join-Path $RunDirectory 'proxy-control.json') @{id=$control;upDelayMs=11000}
                $deadline = [DateTime]::UtcNow.AddSeconds(5)
                do {
                    $proxy = Read-Json (Join-Path $RunDirectory 'proxy-status.json')
                    if ($proxy.appliedControl -eq $control -and $proxy.upDelayMs -eq 11000) { break }
                    if ([DateTime]::UtcNow -gt $deadline) { throw 'Proxy did not apply the directed combat delay.' }
                    Start-Sleep -Milliseconds 100
                } while ($true)
                Invoke-Lab 'guest' @{op='self-damage';amount=1000}
                Invoke-Lab 'host' @{op='damage';player=$before.name;amount=1000}
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='respawn before old damage arrives';seconds=10})}
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.players | Where-Object name -eq $before.name
                if ($source.controller.health -le 0 -or $source.controller.deaths -ne ($before.deaths+1)) {
                    throw 'Expected one death and a living replacement before the old request arrives.'
                }
                $replacement = $source.controller.pawn
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='old request arrives after respawn';seconds=3})}
                $afterOld = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.players | Where-Object name -eq $before.name
                if ($afterOld.controller.pawn -ne $replacement -or $afterOld.controller.health -le 0 -or
                    $afterOld.controller.deaths -ne ($before.deaths+1)) { throw 'Old damage changed the newly respawned pawn.' }
                Write-Json (Join-Path $RunDirectory 'proxy-control.json') @{id=[guid]::NewGuid().ToString('N');upDelayMs=0}
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='delayed reliable stream drains';seconds=12})}
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.players | Where-Object name -eq $before.name
                $owner = (Read-Json (Join-Path $RunDirectory 'guest-status.json')).localController
                Write-Json (Join-Path $RunDirectory 'stale-combat-result.json') @{before=$before;replacement=$replacement;afterOld=$afterOld.controller;source=$source.controller;owner=$owner;delayMs=11000;recoveryReplacedPawn=($source.controller.pawn -ne $replacement)}
                if ($source.controller.health -le 0 -or
                    $source.controller.deaths -ne ($before.deaths+1) -or $owner.deaths -ne $source.controller.deaths -or
                    $owner.health -ne $source.controller.health) { throw 'An old combat request damaged the replacement pawn or owner disagrees.' }
                Invoke-Lab 'guest' @{op='self-damage';amount=10}
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='fresh possession combat still works';seconds=3})}
                $fresh = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.players | Where-Object name -eq $before.name
                if ($fresh.controller.health -ge $source.controller.health -or $fresh.controller.health -le 0) { throw 'Fresh possession damage did not apply normally.' }
            }
            if ($Scenario -eq 'spectator') {
                if ($Hub -or !$SecondObserver -or $Lives -ne 1) { throw 'Spectator progression needs a regular session, -SecondObserver and -Lives 1.' }
                Invoke-Lab $VictimRole @{op='input';steps=@(@{label='spawn protection expires';seconds=10})}
                $victim = (Read-Json (Join-Path $RunDirectory "$VictimRole-status.json")).localController
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $mates = @($source.players | Where-Object { $_.controller.team -eq $victim.team -and $_.name -ne $victim.name })
                if ($mates.Count -ne 2) { throw 'Choose ObserverTeam so both observers are teammates of the victim.' }
                Invoke-Lab 'host' @{op='damage';player=$victim.name;amount=1000}
                $first = Wait-SpectatorTarget $VictimRole $mates.name
                if ($Windowed) { Invoke-Lab $VictimRole @{op='capture'} }
                $watched = $first.localController.target
                $lastMate = @($mates | Where-Object name -ne $watched)[0].name
                $resultRoles = @($roles)
                if ($SpectatorDisconnect -eq 'first') {
                    $leavingRole = if ($watched -eq 'LabObserver') { 'observer' } elseif ($watched -eq 'LabObserver2') { 'observer2' } else { throw 'Only an observer teammate can leave in this fixture.' }
                    Invoke-Lab $leavingRole @{op='leave'}
                    $resultRoles = @($resultRoles | Where-Object { $_ -ne $leavingRole })
                } else { Invoke-Lab 'host' @{op='damage';player=$watched;amount=1000} }
                $second = Wait-SpectatorTarget $VictimRole @($lastMate)
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                if ($source.ended) { throw 'Round ended while one teammate remained alive.' }
                if ($Windowed) { Invoke-Lab $VictimRole @{op='capture'} }
                if ($SpectatorDisconnect -eq 'last') {
                    $leavingRole = if ($lastMate -eq 'LabObserver') { 'observer' } elseif ($lastMate -eq 'LabObserver2') { 'observer2' } else { throw 'Only an observer teammate can leave in this fixture.' }
                    Invoke-Lab $leavingRole @{op='leave'}
                    $resultRoles = @($resultRoles | Where-Object { $_ -ne $leavingRole })
                } else { Invoke-Lab 'host' @{op='damage';player=$lastMate;amount=1000} }
                $deadline = [DateTime]::UtcNow.AddSeconds(20)
                do {
                    $results = @($resultRoles | ForEach-Object { Read-Json (Join-Path $RunDirectory "$_-status.json") })
                    if (@($results | Where-Object { $_.phase -ne 'Results' -or !$_.results }).Count -eq 0) { break }
                    if ([DateTime]::UtcNow -gt $deadline) { throw 'Not all clients reached results after the last teammate died.' }
                    Start-Sleep -Milliseconds 200
                } while ($true)
                foreach ($result in $results) {
                    if ($result.results.WinningTeam -ne 1-$victim.team -or $result.results.WinningReason -ne 2) { throw 'Wrong last-teammate elimination outcome.' }
                }
                Start-Sleep -Seconds 2
                if ($Windowed) { foreach ($roleName in $resultRoles) { Invoke-Lab $roleName @{op='capture'} } }
                Write-Json (Join-Path $RunDirectory 'spectator-result.json') @{victim=$victim;twoAlive=$first.localController;oneAlive=$second.localController;firstHud=$first.hud;secondHud=$second.hud;results=$results.results;damageFixture=$true;disconnectStage=$SpectatorDisconnect}
            }
            if ($Scenario -in @('elimination','roundtrip')) {
                if ($Hub -or $Lives -ne 1) { throw 'Elimination requires a regular session with -Lives 1.' }
                Invoke-Lab $VictimRole @{op='input';steps=@(@{label='spawn protection expires';seconds=10})}
                $victim = (Read-Json (Join-Path $RunDirectory "$VictimRole-status.json")).localController
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $losers = @($source.players | Where-Object { $_.controller.team -eq $victim.team })
                foreach ($loser in $losers) { Invoke-Lab 'host' @{op='damage';player=$loser.name;amount=1000} }
                $deadline = [DateTime]::UtcNow.AddSeconds(20)
                do {
                    $settled = $true
                    foreach ($roleName in $roles) {
                        $status = Read-Json (Join-Path $RunDirectory "$roleName-status.json")
                        if ($status.phase -ne 'Results' -or !$status.results) { $settled = $false }
                    }
                    if ($settled) { break }
                    if ([DateTime]::UtcNow -gt $deadline) { throw 'Not all clients reached round results after team elimination.' }
                    Start-Sleep -Milliseconds 200
                } while ($true)
                $resultRows = @()
                foreach ($roleName in $roles) {
                    $status = Read-Json (Join-Path $RunDirectory "$roleName-status.json")
                    if ($status.results.WinningTeam -ne 1-$victim.team) { throw "$roleName received the wrong winning team." }
                    if ($status.results.WinningReason -ne 2) { throw "$roleName did not report team elimination as the round outcome." }
                    $resultRows += @{role=$roleName;results=$status.results}
                }
                Start-Sleep -Seconds 3
                if ($Windowed) { foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='capture'} } }
                Write-Json (Join-Path $RunDirectory 'elimination-result.json') @{losingTeam=$victim.team;victims=$losers.name;clients=$resultRows;damageFixture=$true}
                if ($Scenario -eq 'roundtrip') {
                    foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='ready-next-round'} }
                    Wait-Playing
                    $secondStartup = Assert-Startup
                    $newRound = @()
                    foreach ($roleName in $roles) {
                        $possessionDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
                        do {
                            $status = Read-Json (Join-Path $RunDirectory "$roleName-status.json")
                            if ($status.localController.pawn -and $status.localController.state -notmatch 'Waiting') { break }
                            if ($status.error -or [DateTimeOffset]::UtcNow -gt $possessionDeadline) { throw "$roleName did not receive its next-round pawn." }
                            Start-Sleep -Milliseconds 100
                        } while ($true)
                        $pc = $status.localController
                        if ($status.phase -ne 'Match' -or $pc.health -le 0 -or $pc.spectating -or $pc.deaths -ne 0 -or $pc.lives -ne $Lives) { throw "$roleName did not reset cleanly for the next round." }
                        $newRound += @{role=$roleName;controller=$pc;movement=$status.movement}
                        if ($Windowed) { Invoke-Lab $roleName @{op='capture'} }
                    }
                    Write-Json (Join-Path $RunDirectory 'roundtrip-result.json') @{startup=$secondStartup;clients=$newRound}
                }
            }
            if ($Scenario -eq 'rejoin') {
                $old = Read-Json (Join-Path $RunDirectory 'guest-status.json')
                Invoke-Lab 'guest' @{op='leave'}
                $deadline = [DateTime]::UtcNow.AddSeconds(20)
                do {
                    $server = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                    if (!@($server.players | Where-Object name -eq 'LabGuest').Count) { break }
                    if ([DateTime]::UtcNow -gt $deadline) { throw 'Authority retained the departed guest.' }
                    Start-Sleep -Milliseconds 200
                } while ($true)
                Invoke-Lab 'host' @{op='input';steps=@(@{label='host continues while guest absent';seconds=2;tap=@('bCrouch')})}
                if ($NetworkProfile -ne 'direct') {
                    $controlId = [guid]::NewGuid().ToString('N')
                    Write-Json (Join-Path $RunDirectory 'proxy-control.json') @{id=$controlId;resetClient=$true}
                    $deadline = [DateTime]::UtcNow.AddSeconds(5)
                    while ((Read-Json (Join-Path $RunDirectory 'proxy-status.json')).appliedControl -ne $controlId) {
                        if ([DateTime]::UtcNow -gt $deadline) { throw 'Proxy did not reset its single-client endpoint before reconnect.' }
                        Start-Sleep -Milliseconds 50
                    }
                }
                Invoke-Lab 'guest' @{op='rejoin'}
                Wait-Playing
                if ($Hub) { $rejoinStartup = Assert-Startup } else { $rejoinStartup = 'NOT RUN: regular match continues during reconnect.' }
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='rejoin jump';seconds=1;tap=@('Jump')},@{label='rejoin landing';seconds=5})}
                $joined = Read-Json (Join-Path $RunDirectory 'guest-status.json')
                $server = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $owner = Own-Pawn $joined
                if (@($server.players | Where-Object name -eq 'LabGuest').Count -ne 1 -or !$owner -or $owner.physics -ne 'PHYS_Walking' -or $joined.error) { throw 'Rejoining guest did not return as exactly one playable grounded participant.' }
                if (@($joined.pawns | Where-Object name -eq 'LabHost').Count -ne 1) { throw 'Rejoining guest has a missing or duplicate host baseline.' }
                if ($Windowed) { foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='capture'} } }
                Write-Json (Join-Path $RunDirectory 'rejoin-result.json') @{startup=$rejoinStartup;before=$old.localController;after=$joined.localController;source=$server}
            }
            if ($Scenario -eq 'ledge') {
                if ($Map -ne 'AquaE' -or $Hub) { throw 'The Seaside ledge fixture requires Aquarius in a regular session.' }
                $spyRole = if ($ReverseRoles) { 'guest' } else { 'host' }
                $spyName = if ($ReverseRoles) { 'LabGuest' } else { 'LabHost' }
                Invoke-Lab $spyRole @{op='input';steps=@(@{label='stand from crouched spawn';seconds=1;tap=@('Jump')},@{label='settle';seconds=4})}
                Invoke-Lab 'host' @{op='place';player=$spyName;position=@(5371.0,3862.0,633.005);yaw=16384}
                Invoke-Lab $spyRole @{op='input';steps=@(@{label='ledge fixture settles';seconds=2})}
                Invoke-Lab $spyRole @{op='face';yaw=16384}
                $since = [DateTimeOffset]::UtcNow
                Invoke-Lab $spyRole @{op='input';steps=@(@{label='jump from Seaside ledge';seconds=2;tap=@('Jump');hold=@('BT_Up')},@{label='Seaside landing';seconds=5})}
                $airborne = @(Trace-Since $spyRole $since | Where-Object { @( $_.pawns | Where-Object { $_.name -eq $spyName -and $_.physics -eq 'PHYS_Falling' }).Count })
                if (!$airborne.Count) { throw 'Seaside drop never produced an airborne sample.' }
                Invoke-Lab $spyRole @{op='face';yaw=32768}
                Invoke-Lab $spyRole @{op='input';steps=@(@{label='walk clear of landing';seconds=1.5;hold=@('BT_Up')},@{label='settle on courtyard';seconds=4})}
                $owner = Own-Pawn (Read-Json (Join-Path $RunDirectory "$spyRole-status.json"))
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object name -eq $spyName
                if ($owner.physics -ne 'PHYS_Walking' -or $source.physics -ne 'PHYS_Walking' -or $owner.state -notmatch 'Walking' -or $owner.position[2] -gt 200) { throw 'Seaside drop did not reach a walking state on the lower floor.' }
                $distance = [Math]::Sqrt([Math]::Pow($owner.position[0]-$source.position[0],2)+[Math]::Pow($owner.position[1]-$source.position[1],2)+[Math]::Pow($owner.position[2]-$source.position[2],2))
                if ($distance -gt 3) { throw "Seaside owner/source position differs by $distance uu." }
                if ($Windowed) { foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='capture'} } }
                Write-Json (Join-Path $RunDirectory 'ledge-result.json') @{spyRole=$spyRole;airborneSamples=$airborne.Count;owner=$owner;source=$source;error=$distance;fixture='Collision-checked placement at Seaside upper ledge, normal Jump and movement input thereafter.'}
            }
            if ($Scenario -eq 'death') {
                if ($Hub) { throw 'Death scenarios require regular-session combat rules.' }
                if ($AirborneDeath -and $DeathStance -ne 'standing') { throw 'Choose airborne or crouched death independently.' }
                Invoke-Lab $VictimRole @{op='input';steps=@(@{label='spawn protection expires';seconds=10})}
                $wantCrouched = $DeathStance -eq 'crouched'
                $initialBody = Own-Pawn (Read-Json (Join-Path $RunDirectory "$VictimRole-status.json"))
                if ($initialBody.crouched -ne $wantCrouched) {
                    Invoke-Lab $VictimRole @{op='input';steps=@(@{label='set death stance';seconds=2;tap=@('bCrouch')})}
                }
                $stanceDeadline = [DateTime]::UtcNow.AddSeconds(5)
                do {
                    $body = Own-Pawn (Read-Json (Join-Path $RunDirectory "$VictimRole-status.json"))
                    $sourceBody = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object name -eq $body.name
                    if ($body.crouched -eq $wantCrouched -and $sourceBody.crouched -eq $wantCrouched) { break }
                    if ([DateTime]::UtcNow -gt $stanceDeadline) { throw "Owner and authority never agreed on $DeathStance before death." }
                    Start-Sleep -Milliseconds 100
                } while ($true)
                $before = Read-Json (Join-Path $RunDirectory "$VictimRole-status.json")
                $victim = $before.localController
                if (!$victim -or !$victim.pawn) { throw 'Death fixture has no possessed victim.' }
                if ($roles -contains 'observer') {
                    $body = Own-Pawn $before
                    $watcher = Own-Pawn (Read-Json (Join-Path $RunDirectory 'observer-status.json'))
                    $viewPositions = @()
                    foreach ($distance in @(160,220,300)) {
                        foreach ($degrees in @(180,90,270,0,135,225,45,315)) {
                            $angle = ($body.yaw * 2 * [Math]::PI / 65536) + $degrees * [Math]::PI / 180
                            $viewPositions += ,@(($body.position[0]+$distance*[Math]::Cos($angle)),($body.position[1]+$distance*[Math]::Sin($angle)),($body.position[2]+$watcher.height-$body.height))
                        }
                    }
                    Invoke-Lab 'host' @{op='place';player='LabObserver';positions=$viewPositions;yaw=0;requireSupport=$true}
                    Invoke-Lab 'observer' @{op='input';steps=@(@{label='death observer settles';seconds=1})}
                    $watcher = Own-Pawn (Read-Json (Join-Path $RunDirectory 'observer-status.json'))
                    $viewYaw = [int]([Math]::Atan2($body.position[1]-$watcher.position[1],$body.position[0]-$watcher.position[0])*65536/(2*[Math]::PI))
                    Invoke-Lab 'observer' @{op='face';yaw=$viewYaw;pitch=-3000}
                    if ($Windowed) {
                        $viewAid = if ($ObserverTeam -eq 1) { 'GadTorcheSwitch' } else { 'SwitchHeadset 1' }
                        Invoke-Lab 'observer' @{op='input';steps=@(@{label='illuminate death observation';seconds=1;tap=@($viewAid)})}
                    }
                }
                $since = [DateTimeOffset]::UtcNow
                $jumpCommand = $null
                if ($AirborneDeath) {
                    Invoke-Lab $VictimRole @{op='input';steps=@(@{label='stand before airborne death';seconds=1;tap=@('Jump')},@{label='settle before airborne death';seconds=4})}
                    $jumpCommand = Send-Lab $VictimRole @{op='input';steps=@(@{label='jump into lethal hit';seconds=2;tap=@('Jump')})}
                    $deadline = [DateTime]::UtcNow.AddSeconds(3)
                    do {
                        $falling = @((Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object { $_.name -eq $victim.name -and $_.physics -eq 'PHYS_Falling' })
                        if ($falling.Count) { break }
                        if ([DateTime]::UtcNow -gt $deadline) { throw 'Jump did not become airborne before the death fixture.' }
                        Start-Sleep -Milliseconds 20
                    } while ($true)
                }
                Invoke-Lab 'host' @{op='damage';player=$victim.name;amount=1000;requireAirborne=[bool]$AirborneDeath}
                if ($jumpCommand) { Wait-Ack $VictimRole $jumpCommand }
                Invoke-Lab $VictimRole @{op='input';steps=@(@{label='death animation';seconds=2})}
                if ($Windowed -and ($roles -contains 'observer')) {
                    $body = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object name -eq $victim.name
                    $watcher = Own-Pawn (Read-Json (Join-Path $RunDirectory 'observer-status.json'))
                    if ($body -and $watcher) {
                        $dx = $body.position[0]-$watcher.position[0]
                        $dy = $body.position[1]-$watcher.position[1]
                        $dz = $body.position[2]-$body.height+20-$watcher.position[2]-$watcher.eyeHeight
                        $viewYaw = [int]([Math]::Atan2($dy,$dx)*65536/(2*[Math]::PI))
                        $viewPitch = [int]([Math]::Atan2($dz,[Math]::Sqrt($dx*$dx+$dy*$dy))*65536/(2*[Math]::PI))
                        Invoke-Lab 'observer' @{op='face';yaw=$viewYaw;pitch=$viewPitch}
                    }
                }
                if ($Windowed) { foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='capture'} } }
                if ($Windowed -and ($roles -contains 'observer')) {
                    Invoke-Lab $VictimRole @{op='input';steps=@(@{label='death pose before body blinking';seconds=0.8})}
                    Invoke-Lab 'observer' @{op='capture'}
                    Invoke-Lab $VictimRole @{op='input';steps=@(@{label='death animation completes';seconds=2.7})}
                } else { Invoke-Lab $VictimRole @{op='input';steps=@(@{label='death animation completes';seconds=3.5})} }
                $corpse = Own-Pawn (Read-Json (Join-Path $RunDirectory "$VictimRole-status.json"))
                $corpseServer = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $corpseSource = @($corpseServer.pawns | Where-Object name -eq $victim.name)[0]
                $corpseError = $null
                if (!$corpseServer.ended) {
                    if (!$corpse -or $corpse.health -gt 0 -or $corpseSource.health -gt 0) { throw 'Death fixture lost its corpse before pose comparison.' }
                    $corpseError = [Math]::Sqrt([Math]::Pow($corpse.position[0]-$corpseSource.position[0],2)+[Math]::Pow($corpse.position[1]-$corpseSource.position[1],2)+[Math]::Pow($corpse.position[2]-$corpseSource.position[2],2))
                    if ($corpseError -gt 3) { throw "Dead owner/source position differs by $corpseError uu." }
                    if ($corpse.physics -eq 'PHYS_Falling' -or $corpseSource.physics -eq 'PHYS_Falling') { throw 'Corpse is still falling after its landing/death animation.' }
                    if ($corpse.channels[0].sequence -ne $corpseSource.channels[0].sequence) { throw 'Owner and authority disagree on the settled death clip.' }
                }
                if ($Windowed) { foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='capture'} } }
                Invoke-Lab $VictimRole @{op='input';steps=@(@{label='corpse and spectator transition';seconds=3.5})}
                if ($Windowed) { Invoke-Lab $VictimRole @{op='capture'} }
                Invoke-Lab $VictimRole @{op='input';steps=@(@{label='respawn or permanent spectator';seconds=13})}
                $after = if ($victim.lives -gt 1) { Wait-Respawn $VictimRole $victim.pawn } else { Read-Json (Join-Path $RunDirectory "$VictimRole-status.json") }
                Assert-NoHealthyDeath $victim.name $since
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $authority = @($source.players | Where-Object name -eq $victim.name)[0].controller
                $samples = @(Trace-Since $VictimRole $since)
                if ($AirborneDeath) {
                    $sourceSamples = @(Trace-Since 'server' $since)
                    $inertSource = @($sourceSamples | Where-Object { @($_.server.players | Where-Object { $_.name -eq $victim.name -and $_.controller.state -eq 'PlayerFallingInerte' }).Count })
                    if (!$inertSource.Count) { throw 'Authority never entered inert falling after the airborne lethal hit.' }
                    # The owner can have landed by the time a delayed lethal hit reaches it. Its transient
                    # falling state may fit between 100 ms samples; the inert landing clip must still play.
                    $inertOwner = @($samples | Where-Object { $_.localController.state -match 'PlayerFallingInerte|s_LandingInerte' -or @( $_.pawns | Where-Object { $_.name -eq $victim.name -and $_.health -le 0 -and $_.channels[0].sequence -eq 'LandStXxBk2' }).Count })
                    if (!$inertOwner.Count) { throw 'Owner never presented the airborne death or its inert landing.' }
                }
                if (!@($samples | Where-Object { $_.localController.health -le 0 -or $_.localController.state -match 'Dead|Spectat' }).Count) { throw 'No death state was observed.' }
                if ($authority.deaths -ne $victim.deaths+1 -or $after.localController.deaths -ne $authority.deaths) { throw 'Death count did not resolve once on authority and owner.' }
                if ($victim.lives -gt 1) {
                    if ($authority.lives -ne $victim.lives-1 -or $after.localController.lives -ne $authority.lives) { throw 'Death did not consume exactly one life.' }
                    if (!$after.localController.pawn -or $after.localController.health -le 0 -or $after.localController.pawn -eq $victim.pawn -or $after.localController.spectating) { throw 'Living player did not respawn into a fresh pawn.' }
                    if (!@($samples | Where-Object { $_.localController.spectating }).Count) { throw 'Respawn delay never exposed spectator state.' }
                } elseif ($victim.lives -eq 1) {
                    if ($authority.lives -ne 0 -or $after.localController.lives -ne 0) { throw 'Final life was not consumed.' }
                    if (!$source.ended -and (!$after.localController.spectating -or $after.localController.health -gt 0)) { throw 'Eliminated player is neither spectating nor at round end.' }
                    $remaining = @($source.players | Where-Object { $_.controller.team -eq $victim.team -and $_.controller.lives -gt 0 })
                    if ($remaining.Count -and $source.ended) { throw 'Round ended while the victim team still had lives.' }
                    if (!$remaining.Count -and (!$source.ended -or $source.winningTeam -eq $victim.team)) { throw 'Exhausted team did not lose the round.' }
                } else { throw 'Choose -Lives 1 or greater for counted-life death assertions.' }
                $survivors = @($source.players | Where-Object { $_.controller.team -eq $victim.team -and $_.name -ne $victim.name -and $_.controller.health -gt 0 })
                if ($survivors.Count -and !@($samples | Where-Object { $_.localController.spectating -and $_.localController.target -in $survivors.name }).Count) { throw 'Victim never spectated a living teammate.' }
                if ($Windowed -and !$source.ended) {
                    $spectatorHud = @($samples | Where-Object { $_.localController.spectating -and $_.hud.Active -and $_.hud.panel -and $_.hud.page -eq 'SpectatorHudPage' })
                    if (!$spectatorHud.Count) { throw 'Spectating never displayed its HUD page.' }
                    if ($survivors.Count -and !@($spectatorHud | Where-Object { $_.hud.Watched -in $survivors.name }).Count) { throw 'Spectator HUD never named the watched teammate.' }
                    if ($victim.lives -gt 1 -and !@($spectatorHud | Where-Object { $_.hud.RespawnIn -gt 0 }).Count) { throw 'Spectator HUD never showed the respawn countdown.' }
                    if ($victim.lives -eq 1 -and !@($spectatorHud | Where-Object { $_.hud.NoRespawn }).Count) { throw 'Spectator HUD did not explain that no respawn remains.' }
                }
                if ($Windowed) { foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='capture'} } }
                Write-Json (Join-Path $RunDirectory 'death-result.json') @{victim=$victim;after=$after.localController;authority=$authority;ended=$source.ended;winningTeam=$source.winningTeam;damageFixture=$true;corpseError=$corpseError;corpse=$corpse;corpseSource=$corpseSource;survivingTeammates=$survivors.Count}
            }
            if ($Scenario -eq 'soak') {
                $began = [DateTimeOffset]::UtcNow
                $cycle = 0
                while (([DateTimeOffset]::UtcNow - $began).TotalSeconds -lt $SoakSeconds) {
                    $cycle++
                    $commands = @{}
                    foreach ($roleName in $roles) {
                        $commands[$roleName] = Send-Lab $roleName @{op='input';steps=@(@{label='soak-jump';seconds=1;tap=@('Jump')},@{label='soak-land';seconds=3},@{label='soak-crouch';seconds=1;tap=@('bCrouch')},@{label='soak-forward';seconds=0.5;hold=@('BT_Up')},@{label='soak-back';seconds=0.5;hold=@('BT_Down')},@{label='soak-stand';seconds=1;tap=@('bCrouch')},@{label='soak-settle';seconds=3})}
                    }
                    if ($NetworkProfile -ne 'direct' -and $cycle % 10 -eq 0) {
                        Write-Json (Join-Path $RunDirectory 'proxy-control.json') @{id=[guid]::NewGuid().ToString('N');blackoutMs=1000}
                    }
                    foreach ($roleName in $roles) { Wait-Ack $roleName $commands[$roleName] }
                    $authority = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                    $measurements = @()
                    foreach ($roleName in $roles) {
                        $state = Read-Json (Join-Path $RunDirectory "$roleName-status.json")
                        $owner = Own-Pawn $state
                        if ($state.error -or !$owner -or $owner.physics -ne 'PHYS_Walking' -or $owner.state -notmatch 'Walking') { throw "Soak cycle ${cycle}: $roleName did not return to walking." }
                        $source = $authority.pawns | Where-Object name -eq $owner.name
                        $distance = [Math]::Sqrt([Math]::Pow($owner.position[0]-$source.position[0],2)+[Math]::Pow($owner.position[1]-$source.position[1],2)+[Math]::Pow($owner.position[2]-$source.position[2],2))
                        if ($distance -gt 3) { throw "Soak cycle ${cycle}: settled $roleName differs from authority by $distance uu." }
                        if ($state.movement.resyncs -ne 0 -or ($state.movement.nextSequence-$state.movement.acknowledged) -ge 60) { throw "Soak cycle ${cycle}: $roleName input did not recover: $($state.movement | ConvertTo-Json -Compress)." }
                        if ($Windowed -and @($state.poseIssues | Where-Object area -eq 'Animation').Count) {
                            throw "Soak cycle ${cycle}: $roleName reported missing or frozen rendered animation; see poseIssues in its status."
                        }
                        $replicas = @()
                        foreach ($remote in @($state.pawns | Where-Object { $_.role -eq 'ROLE_SimulatedProxy' -and $_.health -gt 0 })) {
                            $remoteSource = $authority.pawns | Where-Object name -eq $remote.name
                            if (!$remoteSource) { throw "Soak cycle ${cycle}: $roleName retained an unknown living replica $($remote.name)." }
                            $remoteError = [Math]::Sqrt([Math]::Pow($remote.position[0]-$remoteSource.position[0],2)+[Math]::Pow($remote.position[1]-$remoteSource.position[1],2)+[Math]::Pow($remote.position[2]-$remoteSource.position[2],2))
                            $replicas += @{player=$remote.name;error=$remoteError;poseReady=$remote.poseReady;crouched=$remote.crouched;physics=$remote.physics}
                            if (!$remote.poseReady -or $remoteError -gt 3 -or $remote.crouched -ne $remoteSource.crouched -or $remote.physics -ne $remoteSource.physics) {
                                throw "Soak cycle ${cycle}: settled replica $($remote.name) on $roleName disagrees with authority (distance $remoteError, pose ready $($remote.poseReady))."
                            }
                        }
                        if ($replicas.Count -ne ($roles.Count - 1)) { throw "Soak cycle ${cycle}: $roleName is missing a participating player's living replica." }
                        $measurements += @{role=$roleName;error=$distance;memory=$state.memoryBytes;movement=$state.movement;replicas=$replicas}
                    }
                    Write-Json (Join-Path $RunDirectory 'soak-progress.json') @{cycle=$cycle;seconds=([DateTimeOffset]::UtcNow-$began).TotalSeconds;measurements=$measurements;utc=[DateTimeOffset]::UtcNow}
                    (Read-Json (Join-Path $RunDirectory 'soak-progress.json') | ConvertTo-Json -Depth 16 -Compress) | Add-Content -LiteralPath (Join-Path $RunDirectory 'soak-progress.jsonl') -Encoding utf8
                }
            }
            if ($Scenario -eq 'fire') {
                if ($Hub) { throw 'Weapon damage checks require regular-session combat rules.' }
                $spyRole = if ($ReverseRoles) { 'guest' } else { 'host' }
                $mercRole = if ($ReverseRoles) { 'host' } else { 'guest' }
                $spyName = if ($ReverseRoles) { 'LabGuest' } else { 'LabHost' }
                $mercName = if ($ReverseRoles) { 'LabHost' } else { 'LabGuest' }
                Invoke-Lab $mercRole @{op='input';steps=@(@{label='spawn protection expires';seconds=10})}
                $spy = Own-Pawn (Read-Json (Join-Path $RunDirectory "$spyRole-status.json"))
                if ($spy.crouched -ne ($TargetStance -eq 'crouched')) {
                    Invoke-Lab $spyRole @{op='input';steps=@(@{label='target stance';seconds=2;tap=@('bCrouch')})}
                }
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $spy = $source.pawns | Where-Object name -eq $spyName
                $merc = $source.pawns | Where-Object name -eq $mercName
                $positions = @()
                foreach ($degrees in @(0,90,270,180,45,315,135,225)) {
                    $angle = ($merc.yaw*2*[Math]::PI/65536) + $degrees*[Math]::PI/180
                    $positions += ,@(($merc.position[0]+300*[Math]::Cos($angle)),($merc.position[1]+300*[Math]::Sin($angle)),($merc.position[2]+$spy.height-$merc.height))
                }
                Invoke-Lab 'host' @{op='place';player=$spyName;positions=$positions;yaw=0}
                Invoke-Lab $mercRole @{op='input';steps=@(@{label='target history settles';seconds=2})}
                for ($switch=0; $switch -lt 3; $switch++) {
                    $merc = Own-Pawn (Read-Json (Join-Path $RunDirectory "$mercRole-status.json"))
                    if ($merc.weapon.mode -eq 'ROF_Auto') { break }
                    Invoke-Lab $mercRole @{op='input';steps=@(@{label='auto fire mode';seconds=0.4;tap=@('bROF')})}
                }
                $before = Read-Json (Join-Path $RunDirectory "$mercRole-status.json")
                $merc = Own-Pawn $before
                $shown = @($before.pawns | Where-Object name -eq $spyName)[0]
                if ($merc.weapon.mode -ne 'ROF_Auto' -or $shown.crouched -ne ($TargetStance -eq 'crouched')) { throw 'Auto trigger/target-stance fixture did not settle.' }
                $dx = $shown.position[0]-$merc.position[0]; $dy = $shown.position[1]-$merc.position[1]
                $pitch = [int]([Math]::Atan2($shown.position[2]+10-$merc.position[2]-$merc.eyeHeight,[Math]::Sqrt($dx*$dx+$dy*$dy))*65536/(2*[Math]::PI))
                $yaw = [int]([Math]::Atan2($dy,$dx)*65536/(2*[Math]::PI))
                Invoke-Lab $mercRole @{op='face';yaw=$yaw;pitch=$pitch}
                $targetBefore = (Read-Json (Join-Path $RunDirectory "$spyRole-status.json")).localController
                if ($Windowed) { Invoke-Lab $mercRole @{op='capture'} }
                $trigger = if ($FireSeconds -gt 0) { @{label='rifle automatic fire';seconds=$FireSeconds;hold=@('bFire')} } else { @{label='one rifle round';seconds=0.2;tap=@('bFire')} }
                Invoke-Lab $mercRole @{op='input';steps=@($trigger,@{label='shot acknowledgement';seconds=2})}
                $after = Own-Pawn (Read-Json (Join-Path $RunDirectory "$mercRole-status.json"))
                $targetAfter = (Read-Json (Join-Path $RunDirectory "$spyRole-status.json")).localController
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $sourceMerc = $source.pawns | Where-Object name -eq $mercName
                $sourceSpy = $source.pawns | Where-Object name -eq $spyName
                Write-Json (Join-Path $RunDirectory 'fire-result.json') @{stance=$TargetStance;before=$merc;after=$after;source=$sourceMerc;targetBefore=$targetBefore;targetAfter=$targetAfter;targetSource=$sourceSpy;shownTarget=$shown;lag=$source.lag;placementFixture=$true;shotViaInput=$true}
                $shots = $after.weapon.shot-$merc.weapon.shot
                if (($FireSeconds -eq 0 -and $shots -ne 1) -or ($FireSeconds -gt 0 -and $shots -lt 2) -or $sourceMerc.weapon.shot -ne $after.weapon.shot) { throw 'Trigger input and received shot counts disagree.' }
                if ($targetAfter.health -ge $targetBefore.health -or $targetAfter.health -ne $sourceSpy.health) { throw 'Rifle shot did not damage the displayed target consistently.' }
                if ($after.weapon.clip -ne $merc.weapon.clip-$shots -or $after.weapon.ammo -ne $merc.weapon.ammo-$shots) { throw "The $shots owner shots did not consume exactly $shots rounds after acknowledgement." }
                if ($sourceMerc.weapon.clip -ne $after.weapon.clip -or $sourceMerc.weapon.ammo -ne $after.weapon.ammo) { throw 'Authority and owner ammunition disagree after one shot.' }
                Invoke-Lab $mercRole @{op='input';steps=@(@{label='reload';seconds=5;tap=@('bReload')})}
                $reloaded = Own-Pawn (Read-Json (Join-Path $RunDirectory "$mercRole-status.json"))
                $sourceReloaded = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object name -eq $mercName
                if ($reloaded.weapon.clip -ne $reloaded.weapon.clipMax -or $reloaded.weapon.ammo -ne $after.weapon.ammo -or $sourceReloaded.weapon.clip -ne $reloaded.weapon.clip -or $sourceReloaded.weapon.ammo -ne $reloaded.weapon.ammo) { throw 'Reload did not preserve and synchronize ammunition.' }
                Write-Json (Join-Path $RunDirectory 'reload-result.json') @{owner=$reloaded;source=$sourceReloaded}
            }
            if ($Scenario -eq 'grab') {
                if ($Hub) { throw 'Hub rules disable attacks; use a regular session for grab.' }
                if ($GrabOutcome -ne 'knockout' -and $Lives -lt 2) { throw 'Lethal grab checks require -Lives 2 or greater to verify death and respawn.' }
                $spyRole = if ($ReverseRoles) { 'guest' } else { 'host' }
                $mercRole = if ($ReverseRoles) { 'host' } else { 'guest' }
                $spyName = if ($ReverseRoles) { 'LabGuest' } else { 'LabHost' }
                $mercName = if ($ReverseRoles) { 'LabHost' } else { 'LabGuest' }
                Invoke-Lab $spyRole @{op='input';steps=@(@{label='stand';seconds=0.5;tap=@('Jump')},@{label='settle';seconds=4})}
                $status = Read-Json (Join-Path $RunDirectory 'host-status.json')
                $authority = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $spy = $authority.pawns | Where-Object name -eq $spyName
                $merc = $authority.pawns | Where-Object name -eq $mercName
                $yaw = $merc.yaw
                $angle = $yaw * (2 * [Math]::PI / 65536)
                $position = @(($merc.position[0] - 85 * [Math]::Cos($angle)), ($merc.position[1] - 85 * [Math]::Sin($angle)), ($merc.position[2] + $spy.height - $merc.height))
                Invoke-Lab $spyRole @{op='face';yaw=$yaw}
                Invoke-Lab 'host' @{op='place';player=$spyName;position=$position;yaw=$yaw}
                if ($Observer) {
                    $watcher = Own-Pawn (Read-Json (Join-Path $RunDirectory 'observer-status.json'))
                    $viewPositions = @()
                    foreach ($degrees in @(150,210,90,270,45,315,0,180)) {
                        $viewAngle = $angle + $degrees * [Math]::PI / 180
                        $viewPositions += ,@(($merc.position[0] + 280 * [Math]::Cos($viewAngle)),($merc.position[1] + 280 * [Math]::Sin($viewAngle)),($merc.position[2] + $watcher.height - $merc.height))
                    }
                    Invoke-Lab 'host' @{op='place';player='LabObserver';positions=$viewPositions;yaw=$yaw}
                    Invoke-Lab 'observer' @{op='input';steps=@(@{label='observer-settle';seconds=0.5})}
                    $viewPawn = Own-Pawn (Read-Json (Join-Path $RunDirectory 'observer-status.json'))
                    $viewYaw = [int]([Math]::Atan2($merc.position[1]-$viewPawn.position[1],$merc.position[0]-$viewPawn.position[0]) * 65536 / (2*[Math]::PI))
                    Invoke-Lab 'observer' @{op='face';yaw=$viewYaw}
                }
                Invoke-Lab $spyRole @{op='input';steps=@(@{label='fixture-settle';seconds=1},@{label='grab';seconds=0.2;tap=@('Use')},@{label='catch';seconds=4})}
                $spyState = Read-Json (Join-Path $RunDirectory "$spyRole-status.json")
                $mercState = Read-Json (Join-Path $RunDirectory "$mercRole-status.json")
                $hostState = Read-Json (Join-Path $RunDirectory 'host-status.json')
                $spy = Own-Pawn $spyState
                $merc = Own-Pawn $mercState
                if ($spy.state -ne 's_GrabbingPlayer' -or $merc.state -ne 's_GrabbedByPlayer') {
                    throw "Grab did not reach the hold on both owners: spy=$($spy.state), merc=$($merc.state)"
                }
                $authority = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                $sourceSpy = $authority.pawns | Where-Object name -eq $spyName
                $sourceMerc = $authority.pawns | Where-Object name -eq $mercName
                if ($sourceSpy.state -ne 's_GrabbingPlayer' -or $sourceMerc.state -ne 's_GrabbedByPlayer' -or !$spy.target -or !$merc.attacker) {
                    throw 'Grab owners and authority disagree on the hold or partner references.'
                }
                if ($Observer) {
                    $view = Read-Json (Join-Path $RunDirectory 'observer-status.json')
                    $seenSpy = $view.pawns | Where-Object name -eq $spyName
                    $seenMerc = $view.pawns | Where-Object name -eq $mercName
                    if ($seenSpy.targetName -ne $mercName -or $seenMerc.attackerName -ne $spyName -or $seenMerc.physics -ne 'PHYS_FollowParent') { throw 'Independent observer does not see the paired hold.' }
                }
                if ($Windowed) { foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='capture'} } }
                $health = $merc.health
                $mercBefore = $mercState.localController
                $spyBefore = $spyState.localController
                $releaseSince = [DateTimeOffset]::UtcNow
                if ($GrabOutcome -eq 'knockout') {
                Invoke-Lab $spyRole @{op='input';steps=@(@{label='knockout';seconds=1;tap=@('bFire')},@{label='recover';seconds=21})}
                $hostState = Read-Json (Join-Path $RunDirectory 'host-status.json')
                $mercState = Read-Json (Join-Path $RunDirectory "$mercRole-status.json")
                $merc = Own-Pawn $mercState
                $sourceMerc = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object name -eq $mercName
                if ($merc.health -ne ($health - 51) -or $sourceMerc.health -ne $merc.health -or $merc.state -notmatch 'Walking' -or $merc.attacker) {
                    throw "KO/recovery failed: health=$($merc.health)/$($sourceMerc.health), state=$($merc.state), attacker=$($merc.attacker)"
                }
                if ($Windowed -and $mercState.hudSleeping) {
                    Invoke-Lab $mercRole @{op='capture'}
                    throw 'Merc recovered in gameplay but the rendered sleeping overlay is still active.'
                }
                } else {
                    if ($GrabOutcome -eq 'neck-break') {
                        Invoke-Lab $spyRole @{op='input';steps=@(@{label='neck break';seconds=0.2;tap=@('Use')},@{label='lethal release';seconds=2.5})}
                    } else {
                        Invoke-Lab $spyRole @{op='input';steps=@(@{label='hold until choke timer';seconds=13},@{label='lethal release';seconds=2.5})}
                    }
                    $mercState = Read-Json (Join-Path $RunDirectory "$mercRole-status.json")
                    $corpse = Own-Pawn $mercState
                    $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                    $corpseSource = @($source.pawns | Where-Object name -eq $mercName)[0]
                    $releasedSpy = Own-Pawn (Read-Json (Join-Path $RunDirectory "$spyRole-status.json"))
                    if (!$corpse -or $corpse.health -gt 0 -or $corpseSource.health -gt 0 -or $corpse.attacker -or $releasedSpy.target) { throw 'Lethal grab did not kill and unlink the pair on both owners.' }
                    if ($corpse.physics -eq 'PHYS_FollowParent' -or $corpseSource.physics -eq 'PHYS_FollowParent') { throw 'Released corpse is still attached to the spy.' }
                    $corpseError = [Math]::Sqrt([Math]::Pow($corpse.position[0]-$corpseSource.position[0],2)+[Math]::Pow($corpse.position[1]-$corpseSource.position[1],2)+[Math]::Pow($corpse.position[2]-$corpseSource.position[2],2))
                    if ($corpseError -gt 3) { throw "Lethal-grab corpse differs from authority by $corpseError uu." }
                    if ($Windowed) { foreach ($roleName in $roles) { Invoke-Lab $roleName @{op='capture'} } }
                    Invoke-Lab $spyRole @{op='input';steps=@(@{label='merc spectator and respawn';seconds=20})}
                    $mercState = Wait-Respawn $mercRole $mercBefore.pawn
                    Assert-NoHealthyDeath $mercName $releaseSince
                    $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server
                    $mercAfter = $mercState.localController
                    $mercAuthority = @($source.players | Where-Object name -eq $mercName)[0].controller
                    $spyAuthority = @($source.players | Where-Object name -eq $spyName)[0].controller
                    if ($mercAfter.deaths -ne $mercBefore.deaths+1 -or $mercAuthority.deaths -ne $mercAfter.deaths -or $mercAfter.lives -ne $mercBefore.lives-1 -or $mercAuthority.lives -ne $mercAfter.lives) { throw 'Lethal grab did not consume exactly one life/death on owner and authority.' }
                    if ($spyAuthority.kills -ne $spyBefore.kills+1) { throw 'Lethal grab kill was not credited exactly once.' }
                    if ($mercAfter.health -le 0 -or !$mercAfter.pawn -or $mercAfter.pawn -eq $mercBefore.pawn -or $mercAfter.spectating) { throw 'Merc did not respawn into a fresh playable pawn after lethal grab.' }
                    $samples = @(Trace-Since $mercRole $releaseSince)
                    if (!@($samples | Where-Object { $_.localController.spectating }).Count) { throw 'Lethal-grab respawn delay never entered spectator state.' }
                    Write-Json (Join-Path $RunDirectory 'grab-death-result.json') @{outcome=$GrabOutcome;before=$mercBefore;after=$mercAfter;authority=$mercAuthority;killer=$spyAuthority;corpse=$corpse;corpseSource=$corpseSource;corpseError=$corpseError;placementFixture=$true;releaseViaInput=$true}
                }
            }
            if ($Scenario -eq 'jump') {
                $since = [DateTimeOffset]::UtcNow
                $id = Send-Lab 'host' @{op='input';steps=@(@{label='jump';seconds=1;tap=@('Jump')},@{label='settle';seconds=5})}
                Wait-Ack 'host' $id
                $status = Read-Json (Join-Path $RunDirectory 'host-status.json')
                $pawn = Own-Pawn $status
                if ($pawn.physics -ne 'PHYS_Walking' -or $pawn.state -match 'Falling|Jump') { throw "Jump did not land: $($pawn | ConvertTo-Json -Compress -Depth 4)" }
                $airborne = @(Trace-Since 'host' $since | Where-Object { (Own-Pawn $_).physics -eq 'PHYS_Falling' })
                if ($airborne.Count -eq 0) { throw 'Jump input never produced an airborne sample; landing cannot be certified.' }
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object name -eq 'LabHost'
                if ($source.physics -ne 'PHYS_Walking' -or $source.state -match 'Falling|Jump') { throw 'Authority did not finish landing.' }
            }
            if ($Scenario -eq 'stance') {
                $spyRole = if ($ReverseRoles) { 'guest' } else { 'host' }
                $watchRole = if ($ReverseRoles) { 'host' } else { 'guest' }
                $spyName = if ($ReverseRoles) { 'LabGuest' } else { 'LabHost' }
                foreach ($roleName in @('host','guest')) { Invoke-Lab $roleName @{op='inspect-reset'} }
                Invoke-Lab $spyRole @{op='input';steps=@(@{label='stand-first';seconds=0.5;tap=@('Jump')},@{label='land';seconds=4},@{label='crouch';seconds=2;tap=@('bCrouch')},@{label='walk-crouched';seconds=2;hold=@('BT_Down')},@{label='stop';seconds=2},@{label='stand';seconds=2;tap=@('bCrouch')},@{label='crouch-again';seconds=2;tap=@('bCrouch')})}
                $hostState = Read-Json (Join-Path $RunDirectory 'host-status.json')
                $guestState = Read-Json (Join-Path $RunDirectory 'guest-status.json')
                $owner = Own-Pawn (Read-Json (Join-Path $RunDirectory "$spyRole-status.json"))
                $copy = (Read-Json (Join-Path $RunDirectory "$watchRole-status.json")).pawns | Where-Object name -eq $spyName
                $source = (Read-Json (Join-Path $RunDirectory 'server-status.json')).server.pawns | Where-Object name -eq $spyName
                foreach ($pawn in @($owner,$copy,$source)) {
                    if (!$pawn.crouched -or $pawn.height -ne 50 -or $pawn.pivot[2] -ne 37.5) { throw 'Crouch shape/offset disagrees between owner, observer and authority.' }
                }
                if ($Windowed -and (@($hostState.poseIssues).Count -gt 0 -or @($guestState.poseIssues).Count -gt 0)) {
                    foreach ($roleName in @('host','guest')) { Invoke-Lab $roleName @{op='capture'} }
                    throw 'Rendered pose inspector reported issues; inspect both status files and captures.'
                }
            }
            if ($Scenario -eq 'outage') {
                if ($NetworkProfile -eq 'direct') { throw 'Outage scenario requires a UDP proxy profile.' }
                Write-Json (Join-Path $RunDirectory 'proxy-control.json') @{id=[guid]::NewGuid().ToString('N');blackoutMs=3000}
                Invoke-Lab 'guest' @{op='input';steps=@(@{label='outage-move';seconds=1;hold=@('BT_Up')},@{label='recover';seconds=9})}
                $status = Read-Json (Join-Path $RunDirectory 'guest-status.json')
                if ($status.error -or !(Own-Pawn $status) -or ($status.snapshotTick - $status.renderTick) -gt 24) { throw 'Guest did not recover current state after the UDP outage.' }
            }
            if ($Scenario -eq 'stall') {
                $id = Send-Lab 'guest' @{op='stall';milliseconds=5000}
                Wait-Ack 'guest' $id
                Start-Sleep -Seconds 1
                $status = Read-Json (Join-Path $RunDirectory 'guest-status.json')
                if (($status.snapshotTick - $status.renderTick) -gt 12) { throw 'Guest is still presenting stale server history after its stall.' }
            }
            if ($Windowed) {
                $captureRoles = if ($Scenario -eq 'spectator') { $resultRoles } else { $roles }
                foreach ($roleName in $captureRoles) { $id = Send-Lab $roleName @{op='capture'}; Wait-Ack $roleName $id }
            }
            $passed = $true
        } catch {
            $failure = $_.Exception.Message
            throw
        } finally {
            Write-Json (Join-Path $RunDirectory 'result.json') @{scenario=$Scenario; passed=$passed; startup=$startup; error=$failure; rendered=[bool]$Windowed; networkProfile=$NetworkProfile; completed=[DateTimeOffset]::UtcNow}
            Stop-Lab
        }
        Write-Output "PASS $Scenario : $RunDirectory"
    }
}
