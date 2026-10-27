<#
.SYNOPSIS
    Packs an exported OpenSCCT build and the launcher into release assets, and writes the script that publishes
    them. Publishes nothing itself.

.DESCRIPTION
    A version release (v<version>, the game's version) carries only the launcher, so players see one download.
    Everything the launcher fetches itself goes to the game files release (tag game-files, shared by every version).

    The launcher has its own version (LauncherVersion in Directory.Build.props). It is built only when that version is
    new; otherwise the release carries the exe made before, so players' launchers update themselves only when the
    launcher changed. Launcher sources that changed under a version already made stop the release.

    Writes to local\release\<version>\ (gitignored):
        OpenSCCT.Launcher.exe                            the launcher: the only asset of the version release, labelled
                                                         "OpenSCCT Launcher <launcher version>" (the name has no version,
                                                         so players' copies keep one name across updates)
        game-files\                                       what goes to the game files release:
            OpenSCCT-<version>-win-x64-files.json         the build's file list (delta updates)
            OpenSCCT-<version>-win-x64-files.json.sha256  its SHA-256
            OpenSCCT.Launcher-<launcher version>-win-x64.exe.sha256  the launcher's SHA-256 (named by its version:
                                                         every version shares this release)
            <sha256>.gz, OpenSCCT-<version>-win-x64-pack.zip  the build's new file contents
        notes.md                                         release notes template (when -NotesFile is not given)
        publish.ps1                                      uploads the game files, then makes the draft version release

    The build is checked for original game files and converted content before it is zipped: a release carries our
    code builds only. Game content is built on the player's machine from their own install.

    The launcher (src/OpenScct.Launcher.Core) reads these exact names; see docs/LAUNCHER.md.

.EXAMPLE
    tools\release\New-Release.ps1 -Version 0.2.0
.EXAMPLE
    tools\release\New-Release.ps1 -Version 0.3.0-beta.1 -BuildDir local\testbuild
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # Folder with the exported game (OpenSCCT.exe and its data folder).
    [string]$BuildDir,

    # Launcher exe to attach instead of the kept or newly built one (local\release\launchers\<LauncherVersion>).
    [string]$LauncherExe,

    # Kept so older command lines still work: the launcher is built on its own when its version is new.
    [switch]$PublishLauncher,

    # Leave the launcher out of the release.
    [switch]$SkipLauncher,

    [string]$OutDir,

    [string]$Repo = 'Lumbridge/OpenSCCT',

    # The release that holds every version's game files (ReleasePicker.StoreTag in the launcher).
    [string]$StoreTag = 'game-files',

    [string]$NotesFile,

    # Release whose file list the delta is made against. Default: the newest published release.
    [string]$BaseTag,

    # Make every file a new blob of this release (no delta against an older one).
    [switch]$NoDelta,

    # Leave the content builder out (a player could not build content with this release).
    [switch]$SkipContentTools
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$semver = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
if ($Version.StartsWith('v')) { $Version = $Version.Substring(1) }
if ($Version -notmatch $semver) { throw "Version '$Version' is not MAJOR.MINOR.PATCH[-PRERELEASE]." }
$isPrerelease = $Version.Contains('-')

if (-not $BuildDir) { $BuildDir = Join-Path $repoRoot 'local\build' }
$BuildDir = (Resolve-Path $BuildDir).Path
if (-not (Test-Path (Join-Path $BuildDir 'OpenSCCT.exe'))) {
    throw "No OpenSCCT.exe in $BuildDir. Export the game first (Godot: Windows Desktop preset)."
}
if (-not $OutDir) { $OutDir = Join-Path $repoRoot "local\release\$Version" }

# Releases are built from pushed commits only: warn when the tree or the commit is not.
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$dirty = [bool](& git -C $repoRoot status --porcelain --untracked-files=no)
$pushed = [bool](& git -C $repoRoot branch -r --contains $commit 2>$null)
if ($dirty) { Write-Warning 'The working tree has uncommitted changes; the build may not match a commit.' }
if (-not $pushed) { Write-Warning "Commit $commit is not on any remote branch yet." }

# The game shows the version stamped into its assemblies (BuildInfo): a build exported for another version would
# show the wrong one in game.
$stamped = Get-ChildItem -LiteralPath $BuildDir -Directory -Filter 'data_*' |
    ForEach-Object { Join-Path $_.FullName 'OpenScct.Net.dll' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $stamped) { throw "No data_*\OpenScct.Net.dll in $BuildDir to read the game's version from." }
$gameVersion = ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($stamped).ProductVersion -split '\+')[0]
if ($gameVersion -ne $Version) {
    throw "The build in $BuildDir is version $gameVersion, not $Version. Export the game again from the release commit."
}

# Files that never ship: local window settings, logs, the test launch scripts and notes of a local build (.cmd, .bat,
# BUILD.txt), original game file types, converted content.
$excludeNames = @('override.cfg', 'BUILD.txt')
$gameExt = @('.sdc', '.usx', '.utx', '.utc', '.ukx', '.uax', '.uas', '.mux', '.u', '.gpr', '.bik', '.umd', '.int')
$contentDirs = @('maps', 'svmap', '.content', 'shared', 'packs')

$problems = @()
foreach ($dir in $contentDirs) {
    if (Test-Path (Join-Path $BuildDir $dir)) { $problems += "content folder '$dir' next to the exe" }
}
Get-ChildItem -LiteralPath $BuildDir -Recurse -File | ForEach-Object {
    if ($gameExt -contains $_.Extension.ToLowerInvariant()) {
        $problems += "original game file type: $($_.FullName.Substring($BuildDir.Length + 1))"
    }
}
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    throw 'The build contains game data or converted content; a release carries our code only.'
}

if (Test-Path $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$staging = Join-Path $OutDir '.staging'
New-Item -ItemType Directory -Force $staging | Out-Null

Get-ChildItem -LiteralPath $BuildDir -Recurse -File | ForEach-Object {
    if ($excludeNames -contains $_.Name -or $_.Extension -in @('.log', '.cmd', '.bat')) { return }
    $relative = $_.FullName.Substring($BuildDir.Length + 1)
    $dest = Join-Path $staging $relative
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $dest
}

# The maps the game ships itself (ours, generated; no game data): the hub with its pad config and the test maps.
# The game finds them in maps\ next to the exe (OpenScct.Server.ContentPaths).
foreach ($map in @('hub', 'hubmap', 'testbox')) {
    $from = Join-Path $repoRoot "fixtures\$map"
    if (-not (Test-Path $from)) { throw "fixtures\$map is missing." }
    Copy-Item -LiteralPath $from -Destination (Join-Path $staging "maps\$map") -Recurse
}

# The content builder the launcher runs on the player's install (Build-ContentTools.ps1).
if (-not $SkipContentTools) {
    & (Join-Path $PSScriptRoot 'Build-ContentTools.ps1') -OutDir (Join-Path $staging 'contenttools')
}

[ordered]@{
    version = $Version
    commit  = $commit
    dirty   = $dirty
    built   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging 'openscct-build.json') -Encoding utf8NoBOM

# The project's own guard for game data, when Python is available.
$guard = Join-Path $repoRoot 'tools\ci\no_game_data.ps1'
if (Test-Path $guard) {
    # The guard is written for commits, so a build always trips it on what a build is made of: program binaries
    # ([pe-binary]), the engine exe's size ([size]) and our own maps copied from fixtures\ ([media] under maps\hub,
    # maps\testbox). Anything else it reports stops the release.
    $report = & $guard --all $staging 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    if ($code -eq 1) {
        $problems = $report | Where-Object { $_ -match '^\s+\S.*: \[' } |
            Where-Object { $_ -notmatch '\[(pe-binary|size)\]' -and $_ -notmatch '^\s+maps/(hub|hubmap|testbox)/.*\[media\]' }
        if ($problems) {
            $problems | ForEach-Object { Write-Host $_ -ForegroundColor Red }
            throw 'tools/ci/no_game_data found game data in the build.'
        }
    }
    elseif ($code -ne 0) { Write-Warning 'tools/ci/no_game_data could not run; only the built-in checks were applied.' }
}

$assets = @()
$launcherAsset = $null

# The file list for delta updates (docs/LAUNCHER.md "Delta updates"): every file with its SHA-256 and the release
# asset that holds it gzipped. Files the base release already has keep pointing at their old asset; only the
# others become new blobs of this release.
$known = @{}
if (-not $NoDelta) {
    if (-not $BaseTag) {
        $listed = & gh release list --repo $Repo --exclude-drafts --limit 30 --json tagName,publishedAt 2>$null | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { Write-Warning 'gh could not list releases; every file becomes a new blob.'; $listed = @() }
        $BaseTag = ($listed | Where-Object { $_.tagName -match '^v\d' } | Sort-Object publishedAt -Descending | Select-Object -First 1).tagName
    }
    if ($BaseTag) {
        $baseDir = Join-Path $OutDir '.base'
        $baseVersion = $BaseTag.TrimStart('v')
        & gh release download $StoreTag --repo $Repo --pattern "OpenSCCT-$baseVersion-win-x64-files.json" --dir $baseDir 2>$null
        if (-not (Test-Path (Join-Path $baseDir "OpenSCCT-$baseVersion-win-x64-files.json"))) {
            & gh release download $BaseTag --repo $Repo --pattern '*-files.json' --dir $baseDir 2>$null
        }
        $baseList = Get-ChildItem -LiteralPath $baseDir -Filter '*-files.json' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($baseList) {
            foreach ($f in (Get-Content -LiteralPath $baseList.FullName -Raw | ConvertFrom-Json).files) { $known[$f.sha256] = $f.blob }
            Write-Host "Delta against $BaseTag ($($known.Count) files known)."
        }
        else {
            Write-Warning "$BaseTag has no file list; every file becomes a new blob."
        }
        Remove-Item -LiteralPath $baseDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# New file contents become blobs of this release: a file of 256 KB or more is its own gzipped asset <sha256>.gz; the
# smaller ones go together into one zip pack (entries named by their SHA-256), so a release stays well under GitHub's
# 1000 assets per release.
$blobDir = Join-Path $OutDir 'game-files'
New-Item -ItemType Directory -Force $blobDir | Out-Null
$packLimit = 256KB
$packName = "OpenSCCT-$Version-win-x64-pack.zip"
$packPath = Join-Path $blobDir $packName
$pack = $null
$packRef = [ordered]@{ tag = $StoreTag; name = $packName; size = 0 }
$entries = [System.Collections.Generic.List[object]]::new()
$newBlobs = @{}
$fullSize = 0L
try {
    Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($staging.Length + 1).Replace('\', '/')
        $sha = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $blob = $known[$sha]
        if (-not $blob) { $blob = $newBlobs[$sha] }
        if (-not $blob) {
            if ($_.Length -lt $packLimit) {
                if (-not $pack) { $pack = [System.IO.Compression.ZipFile]::Open($packPath, [System.IO.Compression.ZipArchiveMode]::Create) }
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($pack, $_.FullName, $sha, [System.IO.Compression.CompressionLevel]::Optimal)
                $blob = [ordered]@{ tag = $packRef.tag; name = $packRef.name; size = 0; member = $sha }
            }
            else {
                $gz = Join-Path $blobDir "$sha.gz"
                $in = [System.IO.File]::OpenRead($_.FullName)
                $out = [System.IO.File]::Create($gz)
                $zip = [System.IO.Compression.GZipStream]::new($out, [System.IO.Compression.CompressionLevel]::Optimal)
                try { $in.CopyTo($zip) } finally { $zip.Dispose(); $out.Dispose(); $in.Dispose() }
                $blob = [ordered]@{ tag = $StoreTag; name = "$sha.gz"; size = (Get-Item -LiteralPath $gz).Length }
            }
            $newBlobs[$sha] = $blob
        }
        $fullSize += $_.Length
        $entries.Add([ordered]@{ path = $relative; size = $_.Length; sha256 = $sha; blob = $blob })
    }
}
finally {
    if ($pack) { $pack.Dispose() }
}
# The pack's size is known once it is closed.
if (Test-Path -LiteralPath $packPath) {
    $packSize = (Get-Item -LiteralPath $packPath).Length
    foreach ($b in $newBlobs.Values) { if ($b.Contains('member')) { $b.size = $packSize } }
}
$uploads = @(Get-ChildItem -LiteralPath $blobDir -File)
$filesJson = Join-Path $blobDir "OpenSCCT-$Version-win-x64-files.json"
[ordered]@{ format = 1; version = $Version; commit = $commit; files = $entries } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $filesJson -Encoding utf8NoBOM
$newSize = ($uploads | Measure-Object -Property Length -Sum).Sum
Write-Host ('{0} files, {1:N1} MB; {2} new files in {3} uploads ({4:N1} MB compressed)' -f $entries.Count, ($fullSize / 1MB), $newBlobs.Count, $uploads.Count, ($newSize / 1MB))

Remove-Item -LiteralPath $staging -Recurse -Force

if (-not $SkipLauncher) {
    # The launcher has its own version (LauncherVersion in Directory.Build.props, bumped only when it changes): a game
    # release carries the same launcher exe again, byte for byte, so players' launchers do not update themselves for
    # nothing. Each launcher version's exe and the git trees of its sources are kept in local\release\launchers\; a
    # release refuses launcher sources that changed under a version that was already made.
    $props = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
    if ($props -notmatch '<LauncherVersion>([^<]+)</LauncherVersion>') { throw 'No LauncherVersion in Directory.Build.props.' }
    $launcherVersion = $Matches[1].Trim()
    if ($launcherVersion -notmatch $semver) { throw "LauncherVersion '$launcherVersion' is not MAJOR.MINOR.PATCH[-PRERELEASE]." }
    $sources = 'src/OpenScct.Launcher', 'src/OpenScct.Launcher.Core', 'src/OpenScct.Maps', 'game/branding/icon.ico'
    $fingerprint = ($sources | ForEach-Object { (& git -C $repoRoot rev-parse "${commit}:$_").Trim() }) -join ' '
    # Kept locally (and its checksum published) under the versioned name; released under the plain name.
    $launcherName = "OpenSCCT.Launcher-$launcherVersion-win-x64.exe"
    $launcherExeName = 'OpenSCCT.Launcher.exe'
    # In the main checkout even when the release is made from a linked worktree (which is removed afterwards).
    $mainRoot = Split-Path -Parent ((& git -C $repoRoot rev-parse --path-format=absolute --git-common-dir).Trim())
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
        $project = Join-Path $repoRoot 'src\OpenScct.Launcher\OpenScct.Launcher.csproj'
        $publishDir = Join-Path $repoRoot 'src\OpenScct.Launcher\bin\Release\net10.0-windows\win-x64\publish'
        & dotnet publish $project -c Release -r win-x64 "-p:Version=$launcherVersion" -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish of the launcher failed.' }
        New-Item -ItemType Directory -Force -Path $kept | Out-Null
        Copy-Item -LiteralPath (Join-Path $publishDir 'OpenSCCT.Launcher.exe') -Destination $keptExe -Force
        [ordered]@{
            version = $launcherVersion; commit = $commit; sources = $fingerprint
            sha256 = (Get-FileHash -LiteralPath $keptExe -Algorithm SHA256).Hash.ToLowerInvariant()
        } | ConvertTo-Json | Set-Content -LiteralPath $keptRecord -Encoding utf8NoBOM
        Write-Host "Launcher $launcherVersion is new: built and kept in $kept."
        $LauncherExe = $keptExe
    }
    $stamped = (Get-Item $LauncherExe).VersionInfo.ProductVersion
    if (-not $stamped -or -not $stamped.StartsWith($launcherVersion)) {
        throw "The launcher exe says version '$stamped', not $launcherVersion."
    }
    $launcherAsset = Join-Path $OutDir $launcherExeName
    Copy-Item -LiteralPath $LauncherExe -Destination $launcherAsset -Force
    $assets += $launcherAsset
}

foreach ($file in @($filesJson) + $assets) {
    $name = Split-Path $file -Leaf
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    # The launcher's checksum file carries its version (ReleasePicker.LauncherChecksumName); the line names the asset.
    $sumName = if ($file -eq $launcherAsset) { "$launcherName.sha256" } else { "$name.sha256" }
    [System.IO.File]::WriteAllText((Join-Path $blobDir $sumName), "$hash  $name`n")
}

if (-not $NotesFile) {
    $NotesFile = Join-Path $OutDir 'notes.md'
    @"
## OpenSCCT $Version

-

Built from $commit.

Game content is not included: the launcher builds it from your own Splinter Cell Chaos Theory install.
"@ | Set-Content -LiteralPath $NotesFile -Encoding utf8NoBOM
}

Write-Host ''
Write-Host "Release assets in $OutDir" -ForegroundColor Green
foreach ($asset in $assets) {
    $size = '{0,10:N1} MB' -f ((Get-Item $asset).Length / 1MB)
    Write-Host "  $size  $(Split-Path $asset -Leaf)  (the version release)"
}
$storeFiles = @(Get-ChildItem -LiteralPath $blobDir -File)
Write-Host ("  {0,10:N1} MB  {1} files for the $StoreTag release in game-files\" -f ((($storeFiles | Measure-Object -Property Length -Sum).Sum) / 1MB), $storeFiles.Count)

# The game files are too many for one command line, so the publish steps go in a script to review and run.
# gh's "file#label" sets the asset's label: the launcher reads its version from it (ReleasePicker.LauncherLabelPrefix).
$quoted = ($assets | ForEach-Object {
        if ($_ -eq $launcherAsset) { '"' + $_ + '#OpenSCCT Launcher ' + $launcherVersion + '"' } else { '"' + $_ + '"' }
    }) -join ' '
$pre = if ($isPrerelease) { ' --prerelease' } else { '' }
$publish = Join-Path $OutDir 'publish.ps1'
@"
# Uploads the game files to the $StoreTag release (made once, as a prerelease so it is never "Latest"), then creates
# the draft release v$Version with the launcher only. Check the draft on GitHub, then publish it.
`$ErrorActionPreference = 'Stop'
gh release view $StoreTag --repo $Repo *> `$null
if (`$LASTEXITCODE -ne 0) {
    gh release create $StoreTag --repo $Repo --target $commit --title "Game files (the launcher downloads these)" --prerelease --latest=false --notes "Files the OpenSCCT launcher downloads and checks by itself. You do not need anything here: download the launcher from the latest release instead."
    if (`$LASTEXITCODE -ne 0) { throw 'gh release create $StoreTag failed.' }
}
`$count = [int](gh release view $StoreTag --repo $Repo --json assets --jq '.assets | length')
`$files = @(Get-ChildItem -LiteralPath "$blobDir" -File | ForEach-Object FullName)
if (`$count + `$files.Count -gt 990) { throw "$StoreTag would pass GitHub's 1000 assets per release (`$count there, `$(`$files.Count) new)." }
for (`$i = 0; `$i -lt `$files.Count; `$i += 20) {
    gh release upload $StoreTag @(`$files[`$i..([Math]::Min(`$i + 19, `$files.Count - 1))]) --repo $Repo --clobber
    if (`$LASTEXITCODE -ne 0) { throw 'gh release upload failed; run this script again to retry.' }
}
gh release view v$Version --repo $Repo *> `$null
if (`$LASTEXITCODE -ne 0) {
    gh release create v$Version $quoted --repo $Repo --target $commit --title "OpenSCCT $Version" --notes-file "$NotesFile" --draft$pre
    if (`$LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
}
"@ | Set-Content -LiteralPath $publish -Encoding utf8NoBOM

Write-Host ''
Write-Host "Edit the notes, then publish with (not run by this script; creates a draft to review first):" -ForegroundColor Yellow
Write-Host "  & ""$publish"""
