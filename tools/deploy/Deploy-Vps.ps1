<#
.SYNOPSIS
    Builds and deploys the master, the dedicated server and the hub to the VPS (docs/ONLINE.md "Updating the VPS").

.DESCRIPTION
    For each chosen service: publishes it (linux-x64, self-contained), uploads the program files, then on the VPS
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
#>
[CmdletBinding()]
param(
    [ValidateSet('master', 'server', 'hub', 'hubmaps')]
    [string[]] $Services = @('master', 'server', 'hub'),
    [string] $VpsHost = 'master.lumbridge.co.uk',
    [string] $User = 'ubuntu',
    [string] $Key = (Join-Path $HOME '.ssh/lightsail-eu-west-2.pem'),
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$publish = Join-Path $root 'local/publish'
$ssh = @('-i', $Key, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15')
$target = "$User@$VpsHost"

# Service: (systemd unit, install folder, published program, project).
$table = @{
    master = @{ Unit = 'svm-master'; Dir = '/opt/svm/master'; Program = 'svm-master'; Project = 'src/OpenScct.Master'; Health = $true }
    server = @{ Unit = 'svm-server'; Dir = '/opt/svm/server'; Program = 'svm-server'; Project = 'src/OpenScct.Server.Cli'; Health = $false }
    hub    = @{ Unit = 'svm-hub'; Dir = '/opt/svm/hub'; Program = 'svm-server'; Project = 'src/OpenScct.Server.Cli'; Health = $false; Maps = $true }
    # The hub's maps alone (fixtures/hub, fixtures/hubmap), its program unchanged.
    hubmaps = @{ Unit = 'svm-hub'; Dir = '/opt/svm/hub'; Program = $null; Health = $false; Maps = $true }
}

function Invoke-Checked([string] $what, [scriptblock] $block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." }
}

# ---- build and pack ------------------------------------------------------------------------------------------------
$programs = $Services | ForEach-Object { $table[$_].Program } | Where-Object { $_ } | Sort-Object -Unique
foreach ($program in $programs) {
    $project = ($table.Values | Where-Object { $_.Program -eq $program } | Select-Object -First 1).Project
    $out = Join-Path $publish $program
    if (-not $SkipBuild) {
        Write-Host "Publishing $program..."
        if (Test-Path $out) { Remove-Item -Recurse -Force $out }
        Invoke-Checked "publish $program" { dotnet publish (Join-Path $root $project) -c Release -r linux-x64 --self-contained true -o $out -nologo -v q }
    }

    Invoke-Checked "pack $program" { tar -czf (Join-Path $publish "$program.tgz") -C $out . }
    Write-Host "Uploading $program..."
    Invoke-Checked "upload $program" { scp @ssh (Join-Path $publish "$program.tgz") "${target}:/tmp/$program.tgz" }
}

# The hub's own maps, laid out as the hub folder has them (maps/Hub, maps/hubmap).
$withMaps = @($Services | Where-Object { $table[$_].Maps }).Count -gt 0
if ($withMaps) {
    $stage = Join-Path $publish 'hubmaps'
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    foreach ($pair in @(@('hub', 'Hub'), @('hubmap', 'hubmap'))) {
        $from = Join-Path $root "fixtures/$($pair[0])"
        if (-not (Test-Path $from)) { throw "fixtures/$($pair[0]) is missing." }
        New-Item -ItemType Directory -Force (Join-Path $stage 'maps') | Out-Null
        Copy-Item -LiteralPath $from -Destination (Join-Path $stage "maps/$($pair[1])") -Recurse
    }

    Invoke-Checked 'pack the hub maps' { tar -czf (Join-Path $publish 'svm-hubmaps.tgz') -C $stage maps }
    Write-Host 'Uploading the hub maps...'
    Invoke-Checked 'upload the hub maps' { scp @ssh (Join-Path $publish 'svm-hubmaps.tgz') "${target}:/tmp/svm-hubmaps.tgz" }
}

# ---- install on the VPS ----------------------------------------------------------------------------------------------
$remote = @'
set -u
# pkg / program "-": no program change; maps "-": no maps (else a tgz of maps/Hub and maps/hubmap).
unit=$1; dir=$2; pkg=$3; health=$4; program=$5; maps=$6
hubmaps="maps/Hub maps/hubmap"
stamp=$(date +%Y%m%d-%H%M%S)
backup=/opt/svm/backups/$unit-$stamp.tgz
mapsbackup=/opt/svm/backups/$unit-$stamp-maps.tgz
sudo mkdir -p /opt/svm/backups
sudo systemctl stop "$unit"
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
  ls -1t /opt/svm/backups/$unit-*.tgz 2>/dev/null | grep -v -- '-maps.tgz$' | tail -n +11 | xargs -r sudo rm -f
  ls -1t /opt/svm/backups/$unit-*-maps.tgz 2>/dev/null | tail -n +4 | xargs -r sudo rm -f
  exit 0
fi
echo "$unit did not come back up; restoring $backup"
sudo journalctl -u "$unit" -n 30 --no-pager
sudo systemctl stop "$unit"
sudo tar -xzf "$backup" -C "$dir"
if [ "$maps" != "-" ]; then
  (cd "$dir" && sudo rm -rf $hubmaps) && sudo tar -xzf "$mapsbackup" -C "$dir"
fi
sudo chown -R svm:svm "$dir"
sudo systemctl start "$unit"
exit 1

'@ -replace "`r`n", "`n"

# Sent as a file with Unix line ends (piping text to ssh would add Windows ones).
$script = Join-Path $publish 'deploy-remote.sh'
[IO.File]::WriteAllText($script, $remote)
Invoke-Checked 'upload the install script' { scp @ssh $script "${target}:/tmp/svm-deploy.sh" }

$failed = @()
foreach ($service in $Services) {
    $s = $table[$service]
    Write-Host "Installing $service..."
    $pkg = if ($s.Program) { "/tmp/$($s.Program).tgz" } else { '-' }
    $program = if ($s.Program) { $s.Program } else { '-' }
    $maps = if ($s.Maps) { '/tmp/svm-hubmaps.tgz' } else { '-' }
    ssh @ssh $target "bash /tmp/svm-deploy.sh $($s.Unit) $($s.Dir) $pkg $([int]$s.Health) $program $maps"
    if ($LASTEXITCODE -ne 0) { $failed += $service }
}

ssh @ssh $target "rm -f /tmp/svm-master.tgz /tmp/svm-server.tgz /tmp/svm-hubmaps.tgz /tmp/svm-deploy.sh" | Out-Null
if ($failed.Count -gt 0) { throw "Not updated (restored from backup): $($failed -join ', ')." }
Write-Host "Done: $($Services -join ', ')."
