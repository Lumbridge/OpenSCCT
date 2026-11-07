<#
.SYNOPSIS
    Builds and deploys the master, the dedicated server and the hub to their VPS hosts (docs/ONLINE.md "Updating the VPS").

.DESCRIPTION
    For each chosen service: publishes it (linux-x64, self-contained), uploads the program files, then on its VPS
    stops the service, backs up its folder (program, config and the master's accounts database and ticket key, but not
    the converted maps and characters) to /opt/svm/backups, unpacks the new program over the old one, starts it again
    and checks it is up. A service that does not come back is restored from that backup and restarted.

    Only program files change: configs (svm-master.json, svm-server.json, svm-hub.json), accounts.db, ticket-key.pem
    and the content folders (maps, chars, shared) are kept, except the hub's own maps: a hub deploy also replaces
    maps/Hub and maps/hubmap with fixtures/hub and fixtures/hubmap (the copies the game ships), since players' games
    refuse a hub whose map differs from theirs ("another hub map"). They are backed up with the program. Each service is down for a few seconds; the game retries
    master calls and servers re-register on their next heartbeat.

.EXAMPLE
    pwsh tools/deploy/Deploy-Vps.ps1                    # master, server and hub
    pwsh tools/deploy/Deploy-Vps.ps1 -Services master   # the master alone
    pwsh tools/deploy/Deploy-Vps.ps1 -Services hubmaps  # the hub's maps alone, program unchanged
.EXAMPLE
    pwsh tools/deploy/Deploy-Vps.ps1 -VpsHost staging.example.org  # all selected services on one custom host
.EXAMPLE
    pwsh tools/deploy/Deploy-Vps.ps1 -PackageOnly local/deploy-package
    pwsh tools/deploy/Deploy-Vps.ps1 -FromPackage local/deploy-package
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('master', 'server', 'hub', 'hubmaps')]
    [string[]] $Services = @('master', 'server', 'hub'),
    [string] $VpsHost = 'master.lumbridge.co.uk',
    [string] $User = 'ubuntu',
    [string] $Key = (Join-Path $HOME '.ssh/lightsail-eu-west-2.pem'),
    [switch] $SkipBuild,
    [string] $PackageOnly,
    [string] $FromPackage,
    [string] $ServerVpsHost,
    [string] $HubVpsHost
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$work = Join-Path ([IO.Path]::GetTempPath()) "openscct-deploy-$([guid]::NewGuid().ToString('N'))"
$ssh = @('-i', $Key, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15')
$customVps = $PSBoundParameters.ContainsKey('VpsHost')
if (-not $ServerVpsHost) { $ServerVpsHost = if ($customVps) { $VpsHost } else { '35.176.22.150' } }
if (-not $HubVpsHost) { $HubVpsHost = if ($customVps) { $VpsHost } else { '16.61.131.28' } }

# Service: (systemd unit, install folder, published program, project).
$table = @{
    master = @{ Vps = $VpsHost; Unit = 'svm-master'; Dir = '/opt/svm/master'; Program = 'svm-master'; Project = 'src/OpenScct.Master'; Health = $true }
    server = @{ Vps = $ServerVpsHost; Unit = 'svm-server'; Dir = '/opt/svm/server'; Program = 'svm-server'; Project = 'src/OpenScct.Server.Cli'; Health = $false }
    hub    = @{ Vps = $HubVpsHost; Unit = 'svm-hub'; Dir = '/opt/svm/hub'; Program = 'svm-server'; Project = 'src/OpenScct.Server.Cli'; Health = $false; Maps = $true }
    # The hub's maps alone (fixtures/hub, fixtures/hubmap), its program unchanged.
    hubmaps = @{ Vps = $HubVpsHost; Unit = 'svm-hub'; Dir = '/opt/svm/hub'; Program = $null; Health = $false; Maps = $true }
}
$Services = @($Services | Select-Object -Unique)
if ('hub' -in $Services) { $Services = @($Services | Where-Object { $_ -ne 'hubmaps' }) }
$deployments = @($Services | Group-Object { $table[$_].Vps })

function Invoke-Checked([string] $what, [scriptblock] $block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." }
}

# ---- build and pack ------------------------------------------------------------------------------------------------
$programs = @($Services | ForEach-Object { $table[$_].Program } | Where-Object { $_ } | Sort-Object -Unique)
$withMaps = [bool]($Services | Where-Object { $table[$_].Maps })
$archives = @($programs | ForEach-Object { "$_.tgz" })
if ($withMaps) { $archives += 'svm-hubmaps.tgz' }

function New-Packages {
  foreach ($program in $programs) {
    $project = ($table.Values | Where-Object { $_.Program -eq $program } | Select-Object -First 1).Project
    $out = if ($PackageOnly) { Join-Path $work $program } else { Join-Path $root "local/publish/$program" }
    if (-not $SkipBuild) {
        Write-Host "Publishing $program..."
        if (Test-Path -LiteralPath $out) {
            $resolved = (Resolve-Path -LiteralPath $out).Path
            $publishRoot = [IO.Path]::GetFullPath((Join-Path $root 'local/publish'))
            if ((Split-Path -Parent $resolved) -ne $publishRoot) { throw "Unexpected publish directory: $resolved" }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
        Invoke-Checked "publish $program" { dotnet publish (Join-Path $root $project) -c Release -r linux-x64 --self-contained true -o $out -nologo -v q }
    }

    Invoke-Checked "pack $program" { tar -czf (Join-Path $work "$program.tgz") -C $out . }
  }

  # The hub's own maps, laid out as the hub folder has them (maps/Hub, maps/hubmap).
  if ($withMaps) {
    $stage = Join-Path $work 'hubmaps'
    foreach ($pair in @(@('hub', 'Hub'), @('hubmap', 'hubmap'))) {
        $from = Join-Path $root "fixtures/$($pair[0])"
        if (-not (Test-Path $from)) { throw "fixtures/$($pair[0]) is missing." }
        New-Item -ItemType Directory -Force (Join-Path $stage 'maps') | Out-Null
        Copy-Item -LiteralPath $from -Destination (Join-Path $stage "maps/$($pair[1])") -Recurse
    }

    Invoke-Checked 'pack the hub maps' { tar -czf (Join-Path $work 'svm-hubmaps.tgz') -C $stage maps }
  }
}

# ---- install on the VPS ----------------------------------------------------------------------------------------------
$remote = @'
set -u
# pkg / program "-": no program change; maps "-": no maps (else a tgz of maps/Hub and maps/hubmap).
unit=$1; dir=$2; pkg=$3; health=$4; program=$5; maps=$6
[ "$(systemctl show -p LoadState --value "$unit")" = loaded ] || { echo "$unit is not installed on this host"; exit 1; }
hubmaps="maps/Hub maps/hubmap"
stamp=$(date +%Y%m%d-%H%M%S)
backup=/opt/svm/backups/$unit-$stamp.tgz
mapsbackup=/opt/svm/backups/$unit-$stamp-maps.tgz
sudo mkdir -p /opt/svm/backups
sudo systemctl stop "$unit" || { echo "Could not stop $unit; deployment cancelled"; exit 1; }
sudo tar -czf "$backup" -C "$dir" --exclude=./maps --exclude=./chars --exclude=./shared --exclude=./backups . || { sudo systemctl start "$unit"; echo "backup failed"; exit 1; }
if [ "$maps" != "-" ]; then
  (cd "$dir" && sudo tar -czf "$mapsbackup" --ignore-failed-read $hubmaps) || { sudo systemctl start "$unit"; echo "maps backup failed"; exit 1; }
fi
installed=1
if [ "$pkg" != "-" ]; then
  # Windows tar keeps no executable bits: set them on the program and the runtime's dump helper.
  sudo tar -xzf "$pkg" -C "$dir" && sudo chmod 755 "$dir/$program" "$dir/createdump" || installed=0
fi
if [ "$maps" != "-" ] && [ $installed = 1 ]; then
  (cd "$dir" && sudo rm -rf $hubmaps) && sudo tar -xzf "$maps" -C "$dir" || installed=0
fi
sudo chown -R svm:svm "$dir"
[ $installed = 1 ] && sudo systemctl start "$unit"
ok=0
for i in $(seq 1 15); do
  sleep 1
  if systemctl is-active --quiet "$unit"; then
    if [ "$health" = "1" ]; then
      url=$(sudo grep -o '"HttpUrl"[^"]*"[^"]*"' "$dir/svm-master.json" | sed -E 's/.*"(http[^"]*)"/\1/')
      curl -fsS "${url:-http://127.0.0.1:28800}/api/v1/status" >/dev/null 2>&1 && { ok=1; break; }
    else
      [ $i -ge 4 ] && { ok=1; break; }
    fi
  fi
done
if [ $ok = 1 ]; then
  echo "$unit updated (backup $backup)"
  sudo sh -c 'ls -1t /opt/svm/backups/"$1"-*.tgz' _ "$unit" 2>/dev/null | grep -v -- '-maps.tgz$' | tail -n +11 | xargs -r sudo rm -f
  sudo sh -c 'ls -1t /opt/svm/backups/"$1"-*-maps.tgz' _ "$unit" 2>/dev/null | tail -n +4 | xargs -r sudo rm -f
  exit 0
fi
echo "$unit did not come back up; restoring $backup"
sudo journalctl -u "$unit" -n 30 --no-pager
sudo systemctl stop "$unit" || { echo "Could not stop $unit; rollback cancelled"; exit 1; }
# Restore a matched database/binary pair. A newer SQLite WAL must never be replayed into the old database.
# Only remove sidecars for databases present in this service's stopped-state backup.
if [ "$program" = "svm-master" ]; then
  sudo python3 - "$dir" "$backup" <<'PY' || { echo "Database journal cleanup failed; service remains stopped"; exit 1; }
import pathlib, sys, tarfile
root = pathlib.Path(sys.argv[1]).resolve()
with tarfile.open(sys.argv[2], 'r:gz') as archive:
    databases = [m.name for m in archive.getmembers() if m.isfile() and m.name.endswith(('.db', '.sqlite', '.sqlite3'))]
for name in databases:
    database = (root / name).resolve()
    if not database.is_relative_to(root):
        raise SystemExit('Backup database path escapes the service directory')
    for suffix in ('-wal', '-shm'):
        pathlib.Path(str(database) + suffix).unlink(missing_ok=True)
PY
fi
sudo tar -xzf "$backup" -C "$dir" || { echo "Backup restore failed; service remains stopped"; exit 1; }
if [ "$maps" != "-" ]; then
  (cd "$dir" && sudo rm -rf $hubmaps) && sudo tar -xzf "$mapsbackup" -C "$dir" || { echo "Map restore failed; service remains stopped"; exit 1; }
fi
sudo chown -R svm:svm "$dir" || { echo "Ownership restore failed; service remains stopped"; exit 1; }
sudo systemctl start "$unit" || { echo "Restored service failed to start"; exit 1; }
exit 1

'@ -replace "`r`n", "`n"

if (($PackageOnly -and $FromPackage) -or ($SkipBuild -and ($PackageOnly -or $FromPackage))) {
    throw 'Choose only one of -PackageOnly, -FromPackage and -SkipBuild.'
}
$commit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not determine the deployment commit.' }
if ($PackageOnly) {
    $dirty = & git -C $root status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Build a deploy package from a clean checkout.' }
    if (Test-Path -LiteralPath $PackageOnly) { throw "Package output already exists: $PackageOnly" }
}

$timer = [Diagnostics.Stopwatch]::StartNew()
New-Item -ItemType Directory $work | Out-Null
try {
    if ($FromPackage) {
        $package = (Resolve-Path -LiteralPath $FromPackage).Path
        $manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json -AsHashtable
        if ($manifest.commit -cne $commit) { throw "Package commit $($manifest.commit) differs from checkout $commit." }
        foreach ($archive in $archives) {
            $copy = Join-Path $work $archive
            Copy-Item -LiteralPath (Join-Path $package $archive) -Destination $copy
            if ((Get-FileHash -LiteralPath $copy).Hash -ne $manifest.sha256[$archive]) { throw "Hash mismatch: $archive" }
        }
    } else {
        New-Packages
    }
    Write-Host ("Packages ready in {0:n1}s" -f $timer.Elapsed.TotalSeconds)

    if ($PackageOnly) {
        $output = New-Item -ItemType Directory $PackageOnly
        $hashes = [ordered]@{}
        foreach ($archive in $archives) {
            $file = Join-Path $work $archive
            $hashes[$archive] = (Get-FileHash -LiteralPath $file).Hash
            Copy-Item -LiteralPath $file -Destination $output.FullName
        }
        @{ commit = $commit; sha256 = $hashes } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output.FullName 'manifest.json')
        return
    }

    # Each attempt uses its own private staging directory on each host.
    $remoteDir = "/tmp/$([IO.Path]::GetFileName($work))"
    $script = Join-Path $work 'svm-deploy.sh'
    [IO.File]::WriteAllText($script, $remote)
    $timer.Restart()
    try {
        $uploadErrors = @($deployments | ForEach-Object -ThrottleLimit 3 -Parallel {
            $target = "$($using:User)@$($_.Name)"
            $sshArgs = $using:ssh
            $stage = $using:remoteDir
            $services = $using:table
            $localStage = $using:work
            $files = @($_.Group | ForEach-Object {
                $service = $services[$_]
                if ($service.Program) { "$($service.Program).tgz" }
                if ($service.Maps) { 'svm-hubmaps.tgz' }
            } | Select-Object -Unique | ForEach-Object { Join-Path $localStage $_ })
            try {
                $ErrorActionPreference = 'Stop'
                ssh @sshArgs $target "mkdir -m 700 '$stage'" | Out-Host
                if ($LASTEXITCODE -ne 0) { throw "Could not create staging directory (exit $LASTEXITCODE)." }
                scp @sshArgs @files $using:script "${target}:${stage}/" | Out-Host
                if ($LASTEXITCODE -ne 0) { throw "Could not transfer packages (exit $LASTEXITCODE)." }
            } catch {
                Write-Warning "${target}: $_"
                $target
            }
        })
        if ($uploadErrors.Count) { throw "Upload failed for $($uploadErrors -join ', ')." }
        Write-Host ("Uploads completed in {0:n1}s" -f $timer.Elapsed.TotalSeconds)

        $timer.Restart()
        # Install the dependency first. Keep services on a shared staging host sequential.
        $waves = @()
        if ('master' -in $Services) { $waves += ,@('master') }
        $workers = @($Services | Where-Object { $_ -ne 'master' })
        if ($workers.Count) { $waves += ,$workers }
        foreach ($wave in $waves) {
            $failed = @($wave | Group-Object { $table[$_].Vps } | ForEach-Object -ThrottleLimit 2 -Parallel {
                $services = $using:table
                $target = "$($using:User)@$($_.Name)"
                $sshArgs = $using:ssh
                $stage = $using:remoteDir
                foreach ($service in $_.Group) {
                    $s = $services[$service]
                    Write-Host "Installing $service on $target..."
                    $pkg = if ($s.Program) { "$stage/$($s.Program).tgz" } else { '-' }
                    $program = if ($s.Program) { $s.Program } else { '-' }
                    $maps = if ($s.Maps) { "$stage/svm-hubmaps.tgz" } else { '-' }
                    try {
                        $ErrorActionPreference = 'Stop'
                        ssh @sshArgs $target "bash '$stage/svm-deploy.sh' $($s.Unit) $($s.Dir) $pkg $([int]$s.Health) $program $maps" | Out-Host
                        if ($LASTEXITCODE -ne 0) { throw "Remote installer exited with $LASTEXITCODE." }
                    } catch {
                        Write-Warning "${service}: $_"
                        $service
                    }
                }
            })
            if ($failed.Count) { throw "Installation failed for $($failed -join ', '). Check the remote status above." }
        }
        Write-Host ("Services installed in {0:n1}s" -f $timer.Elapsed.TotalSeconds)
    } finally {
        $deployments | ForEach-Object -ThrottleLimit 3 -Parallel {
            $target = "$($using:User)@$($_.Name)"
            $sshArgs = $using:ssh
            $stage = $using:remoteDir
            ssh @sshArgs $target "if test -d '$stage'; then rm -f '$stage/svm-master.tgz' '$stage/svm-server.tgz' '$stage/svm-hubmaps.tgz' '$stage/svm-deploy.sh' && rmdir '$stage'; fi" | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not clean up staging on $target." }
        }
    }
    Write-Host "Done: $($Services -join ', ')."
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force
}
