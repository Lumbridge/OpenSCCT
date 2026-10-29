# The launcher exe of a release, shared by New-Release.ps1 and Update-Launcher.ps1 (dot-source it).
#
# The launcher has its own version (LauncherVersion in Directory.Build.props, bumped only when it changes): a game
# release carries the same launcher exe again, byte for byte, so players' launchers do not update themselves for
# nothing. Each launcher version's exe and the git trees of its sources are kept in local\release\launchers\; a
# release refuses launcher sources that changed under a version that was already made.

function Resolve-LauncherExe {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        # The commit the release is made from (its launcher sources are fingerprinted).
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$Repo,
        # An exe to use as this launcher version as given (skips the build and the kept copy).
        [string]$LauncherExe
    )

    $semver = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
    $props = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'Directory.Build.props')
    if ($props -notmatch '<LauncherVersion>([^<]+)</LauncherVersion>') { throw 'No LauncherVersion in Directory.Build.props.' }
    $launcherVersion = $Matches[1].Trim()
    if ($launcherVersion -notmatch $semver) { throw "LauncherVersion '$launcherVersion' is not MAJOR.MINOR.PATCH[-PRERELEASE]." }
    $sources = 'src/OpenScct.Launcher', 'src/OpenScct.Launcher.Core', 'src/OpenScct.Maps', 'game/branding/icon.ico'
    $fingerprint = ($sources | ForEach-Object { (& git -C $RepoRoot rev-parse "${Commit}:$_").Trim() }) -join ' '
    # Kept locally (and its checksum published) under the versioned name; released under the plain name.
    $launcherName = "OpenSCCT.Launcher-$launcherVersion-win-x64.exe"
    $launcherExeName = 'OpenSCCT.Launcher.exe'
    # In the main checkout even when the release is made from a linked worktree (which is removed afterwards).
    $mainRoot = Split-Path -Parent ((& git -C $RepoRoot rev-parse --path-format=absolute --git-common-dir).Trim())
    $kept = Join-Path $mainRoot "local\release\launchers\$launcherVersion"
    $keptExe = Join-Path $kept $launcherName
    $keptRecord = Join-Path $kept 'launcher.json'
    if ($LauncherExe) {
        Write-Warning "Using $LauncherExe as launcher $launcherVersion as given."
    }
    elseif (Test-Path -LiteralPath $keptRecord) {
        $record = Get-Content -Raw -LiteralPath $keptRecord | ConvertFrom-Json
        if ($record.sources -ne $fingerprint) {
            throw "The launcher's sources changed since launcher $launcherVersion was made (commit $($record.commit)). Bump LauncherVersion in Directory.Build.props."
        }
        if (-not (Test-Path -LiteralPath $keptExe)) {
            # Older releases name it by version; newer ones carry the plain name (checked against the record below).
            & gh release download --repo $Repo --pattern $launcherName --dir $kept 2>$null
            if (-not (Test-Path -LiteralPath $keptExe)) {
                & gh release download --repo $Repo --pattern $launcherExeName --dir $kept 2>$null
                $plain = Join-Path $kept $launcherExeName
                if (Test-Path -LiteralPath $plain) { Move-Item -LiteralPath $plain -Destination $keptExe -Force }
            }
        }
        if (-not (Test-Path -LiteralPath $keptExe) -or (Get-FileHash -LiteralPath $keptExe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $record.sha256) {
            throw "Launcher $launcherVersion's exe is missing from $kept (or differs from the one made). Put the released $launcherName there."
        }
        Write-Host "Launcher $launcherVersion unchanged: the release carries the same exe again."
        $LauncherExe = $keptExe
    }
    else {
        # A launcher version not made here yet: build it once and keep it.
        $project = Join-Path $RepoRoot 'src\OpenScct.Launcher\OpenScct.Launcher.csproj'
        $publishDir = Join-Path $RepoRoot 'src\OpenScct.Launcher\bin\Release\net10.0-windows\win-x64\publish'
        & dotnet publish $project -c Release -r win-x64 "-p:Version=$launcherVersion" -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish of the launcher failed.' }
        New-Item -ItemType Directory -Force -Path $kept | Out-Null
        Copy-Item -LiteralPath (Join-Path $publishDir 'OpenSCCT.Launcher.exe') -Destination $keptExe -Force
        [ordered]@{
            version = $launcherVersion; commit = $Commit; sources = $fingerprint
            sha256 = (Get-FileHash -LiteralPath $keptExe -Algorithm SHA256).Hash.ToLowerInvariant()
        } | ConvertTo-Json | Set-Content -LiteralPath $keptRecord -Encoding utf8NoBOM
        Write-Host "Launcher $launcherVersion is new: built and kept in $kept."
        $LauncherExe = $keptExe
    }
    $stamped = (Get-Item $LauncherExe).VersionInfo.ProductVersion
    if (-not $stamped -or -not $stamped.StartsWith($launcherVersion)) {
        throw "The launcher exe says version '$stamped', not $launcherVersion."
    }

    [pscustomobject]@{ Version = $launcherVersion; Exe = $LauncherExe; VersionedName = $launcherName; ExeName = $launcherExeName }
}
