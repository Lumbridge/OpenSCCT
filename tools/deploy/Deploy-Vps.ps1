<#
.SYNOPSIS
    Builds and deploys the master, the dedicated server and the hub to the VPS (docs/ONLINE.md "Updating the VPS").

.DESCRIPTION
    For each chosen service: publishes it (linux-x64, self-contained), uploads the program files, then on the VPS
    stops the service, backs up its folder (program, config and the master's accounts database and ticket key, but not
    the converted maps and characters) to /opt/svm/backups, unpacks the new program over the old one, starts it again
    and checks it is up. A service that does not come back is restored from that backup and restarted.

    Only program files change: configs (svm-master.json, svm-server.json, svm-hub.json), accounts.db, ticket-key.pem
    and the content folders (maps, chars, shared) are kept. Each service is down for a few seconds; the game retries
    master calls and servers re-register on their next heartbeat.

.EXAMPLE
    pwsh tools/deploy/Deploy-Vps.ps1                    # master, server and hub
    pwsh tools/deploy/Deploy-Vps.ps1 -Services master   # the master alone
#>
[CmdletBinding()]
param(
    [ValidateSet('master', 'server', 'hub')]
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
    hub    = @{ Unit = 'svm-hub'; Dir = '/opt/svm/hub'; Program = 'svm-server'; Project = 'src/OpenScct.Server.Cli'; Health = $false }
}

function Invoke-Checked([string] $what, [scriptblock] $block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." }
}

# ---- build and pack ------------------------------------------------------------------------------------------------
$programs = $Services | ForEach-Object { $table[$_].Program } | Sort-Object -Unique
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

# ---- install on the VPS ----------------------------------------------------------------------------------------------
$remote = @'
set -u
unit=$1; dir=$2; pkg=$3; health=$4; program=$5
stamp=$(date +%Y%m%d-%H%M%S)
backup=/opt/svm/backups/$unit-$stamp.tgz
sudo mkdir -p /opt/svm/backups
sudo systemctl stop "$unit"
sudo tar -czf "$backup" -C "$dir" --exclude=./maps --exclude=./chars --exclude=./shared --exclude=./backups . || { sudo systemctl start "$unit"; echo "backup failed"; exit 1; }
# Windows tar keeps no executable bits: set them on the program and the runtime's dump helper.
sudo tar -xzf "$pkg" -C "$dir" && sudo chown -R svm:svm "$dir" && sudo chmod 755 "$dir/$program" "$dir/createdump"
sudo systemctl start "$unit"
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
  ls -1t /opt/svm/backups/$unit-*.tgz 2>/dev/null | tail -n +11 | xargs -r sudo rm -f
  exit 0
fi
echo "$unit did not come back up; restoring $backup"
sudo journalctl -u "$unit" -n 30 --no-pager
sudo systemctl stop "$unit"
sudo tar -xzf "$backup" -C "$dir" && sudo chown -R svm:svm "$dir"
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
    ssh @ssh $target "bash /tmp/svm-deploy.sh $($s.Unit) $($s.Dir) /tmp/$($s.Program).tgz $([int]$s.Health) $($s.Program)"
    if ($LASTEXITCODE -ne 0) { $failed += $service }
}

ssh @ssh $target "rm -f /tmp/svm-master.tgz /tmp/svm-server.tgz /tmp/svm-deploy.sh" | Out-Null
if ($failed.Count -gt 0) { throw "Not updated (restored from backup): $($failed -join ', ')." }
Write-Host "Done: $($Services -join ', ')."
