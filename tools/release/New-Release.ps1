<#
.SYNOPSIS
    Packs an exported OpenSCCT build and the launcher into release assets, and writes the script that publishes
    them. Publishes nothing itself.

.DESCRIPTION
    By default a version release (v<version>, the game's version) carries only the launcher.
    Everything the launcher fetches itself goes to the game files release (tag game-files, shared by every version).
    With another -StoreTag, update metadata also goes on the version release so existing launchers can find it;
    the file list routes new blobs to that store and retains older blobs at their original locations.

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
.EXAMPLE
    pwsh tools/release/New-Release.ps1 -Version 0.8.85 -Platform linux-x64 -BuildDir local/linux -LauncherExe local/dist/OpenSCCT.Launcher-linux-x64
    Prepares native Linux assets beside the Windows preparation. Run both generated publish scripts before
    publishing the shared draft. Linux excludes the Windows content converter and uses downloaded packs.
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Platform = 'win-x64',

    # Folder with the exported game (OpenSCCT.exe or OpenSCCT.x86_64 and its data folder).
    [string]$BuildDir,

    # Launcher exe to attach instead of the kept or newly built one (local\release\launchers\<LauncherVersion>).
    [string]$LauncherExe,

    # Kept so older command lines still work: the launcher is built on its own when its version is new.
    [switch]$PublishLauncher,

    # Leave the launcher out of the release.
    [switch]$SkipLauncher,

    [string]$OutDir,

    [string]$Repo = 'Lumbridge/OpenSCCT',

    # The release that holds new game files. A custom store also puts discoverable metadata on the version release.
    [string]$StoreTag = 'game-files-3',

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

$linux = $Platform -eq 'linux-x64'
$gameExe = if ($linux) { 'OpenSCCT.x86_64' } else { 'OpenSCCT.exe' }
if (-not $BuildDir) { $BuildDir = Join-Path $repoRoot $(if ($linux) { 'local/linux' } else { 'local/build' }) }
$BuildDir = (Resolve-Path $BuildDir).Path
if (-not (Test-Path (Join-Path $BuildDir $gameExe))) {
    throw "No $gameExe in $BuildDir. Export the matching Godot preset first."
}
if (-not $OutDir) { $OutDir = Join-Path $repoRoot $(if ($linux) { "local/release/$Version-linux-x64" } else { "local/release/$Version" }) }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
# This tool recreates its output folder. Reject an ancestor of either the checkout or export.
$outPrefix = $OutDir.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($protected in @($repoRoot, $BuildDir)) {
    if ($protected -eq $OutDir -or $protected.StartsWith($outPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutDir must not contain the source checkout or exported build.'
    }
}

# Releases are built from pushed commits only: warn when the tree or the commit is not.
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$dirty = [bool](& git -C $repoRoot status --porcelain --untracked-files=no)
$pushed = [bool](& git -C $repoRoot branch -r --contains $commit 2>$null)
if ($dirty) { Write-Warning 'The working tree has uncommitted changes; the build may not match a commit.' }
if (-not $pushed) { Write-Warning "Commit $commit is not on any remote branch yet." }

$buildRecord = Join-Path $BuildDir 'openscct-build.json'
if (Test-Path -LiteralPath $buildRecord) {
    $record = Get-Content -LiteralPath $buildRecord -Raw | ConvertFrom-Json
    if ($record.commit -cne $commit -or $record.platform -ne $Platform -or $record.dirty) {
        throw "Export provenance does not match $Platform at $commit. Use the candidate artifact for this checkout."
    }
}

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
    if ($linux -and $dir -eq 'maps') {
        # build.sh adds these generated fixtures. Re-copy the canonical fixtures below, and reject any extras.
        $mapRoot = Join-Path $BuildDir 'maps'
        if (Test-Path -LiteralPath $mapRoot) {
            Get-ChildItem -LiteralPath $mapRoot -Recurse -File | ForEach-Object {
                $relativeMap = $_.FullName.Substring($mapRoot.Length + 1)
                $expected = Join-Path (Join-Path $repoRoot 'fixtures') $relativeMap
                if (($relativeMap -split '[/\\]')[0] -notin @('hub', 'hubmap', 'testbox') -or
                    -not (Test-Path -LiteralPath $expected -PathType Leaf) -or
                    (Get-FileHash -LiteralPath $_.FullName).Hash -ne (Get-FileHash -LiteralPath $expected).Hash) {
                    $problems += "unexpected or modified map: $relativeMap"
                }
            }
        }
    }
    elseif (Test-Path (Join-Path $BuildDir $dir)) { $problems += "content folder '$dir' next to the exe" }
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
    if ($linux -and ($relative -split '[/\\]')[0] -eq 'maps') { return }
    $dest = Join-Path $staging $relative
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $dest
}

# Attribution for the bundled event-feed SVG artwork.
$gameIconsLicense = Join-Path $repoRoot 'packaging\licenses\GameIcons-LICENSE.txt'
if (-not (Test-Path -LiteralPath $gameIconsLicense)) { throw 'packaging\licenses\GameIcons-LICENSE.txt is missing.' }
$licensesDir = Join-Path $staging 'licenses'
New-Item -ItemType Directory -Force $licensesDir | Out-Null
Copy-Item -LiteralPath $gameIconsLicense -Destination (Join-Path $licensesDir 'GameIcons-LICENSE.txt') -Force
$nativeAudioLicenses = Join-Path $repoRoot 'game/addons/scct_audio/licenses'
$nativeSourcesPath = Join-Path $nativeAudioLicenses 'native-audio-sources.txt'
if (!(Test-Path -LiteralPath $nativeSourcesPath)) {
    $audioBuild = if ($linux) { 'tools/audio/build-native-audio.sh' } else { 'tools/audio/Build-NativeAudio.ps1' }
    throw "Native audio licenses and corresponding source are missing. Run $audioBuild before exporting."
}
$nativeSources = Get-Content -LiteralPath $nativeSourcesPath -Raw
$nativePlatform = if ($linux) { 'linux-x86_64' } else { 'windows-x86_64' }
$nativeLibrary = if ($linux) { 'libscct_audio.so' } else { 'scct_audio.dll' }
$nativePlatformDir = Join-Path $repoRoot "game/addons/scct_audio/bin/$nativePlatform"
$nativeBuiltLibrary = Join-Path $nativePlatformDir $nativeLibrary
$nativeExportedLibrary = Join-Path $BuildDir $nativeLibrary
if (!(Test-Path -LiteralPath $nativeBuiltLibrary) -or !(Test-Path -LiteralPath $nativeExportedLibrary) -or
    (Get-FileHash -LiteralPath $nativeBuiltLibrary).Hash -ne (Get-FileHash -LiteralPath $nativeExportedLibrary).Hash) {
    throw 'Exported native audio does not match its staged dependency manifest. Rebuild native audio and export the matching Godot preset.'
}
$nativeLicenseNames = @('godot-cpp-LICENSE.md', 'native-audio-sources.txt')
if ($nativeSources.Contains('reAL HRvb')) {
    $nativeLicenseNames += @('reAL-LICENSE.txt', 'reAL-HRTF-LICENSE.txt')
} elseif ($nativeSources.Contains('OpenAL Soft')) {
    $nativeLicenseNames += @('OpenAL-Soft-COPYING.txt', 'LICENSE-pffft', 'fmt-LICENSE.txt', 'patch_openal_callback.py', 'OpenAL-Soft-1.24.3-source.zip')
    # The comparison backend is dynamically replaceable. Production HRvb needs no companion library.
    $nativeDependencySource = Join-Path $nativePlatformDir $(if ($linux) { 'libopenal.so.1.24.3' } else { 'OpenAL32.dll' })
    $nativeDependencyName = if ($linux) { 'libopenal.so.1' } else { 'OpenAL32.dll' }
    if (!(Test-Path -LiteralPath $nativeDependencySource)) { throw "Native audio dependency missing: $nativeDependencySource" }
    Copy-Item -LiteralPath $nativeDependencySource -Destination (Join-Path $staging $nativeDependencyName) -Force
} else { throw 'Unknown native audio backend in its dependency manifest.' }
foreach ($nativeLicenseName in $nativeLicenseNames) {
    $nativeLicensePath = Join-Path $nativeAudioLicenses $nativeLicenseName
    if (!(Test-Path -LiteralPath $nativeLicensePath)) { throw "Native audio license/source missing: $nativeLicenseName" }
    Copy-Item -LiteralPath $nativeLicensePath -Destination $licensesDir -Force
}

# The maps the game ships itself (ours, generated; no game data): the hub with its pad config and the test maps.
# The game finds them in maps\ next to the exe (OpenScct.Server.ContentPaths).
foreach ($map in @('hub', 'hubmap', 'testbox')) {
    $from = Join-Path $repoRoot "fixtures\$map"
    if (-not (Test-Path $from)) { throw "fixtures\$map is missing." }
    Copy-Item -LiteralPath $from -Destination (Join-Path $staging "maps\$map") -Recurse
}

# The content builder the launcher runs on the player's install (Build-ContentTools.ps1).
if (-not $SkipContentTools -and -not $linux) {
    & (Join-Path $PSScriptRoot 'Build-ContentTools.ps1') -OutDir (Join-Path $staging 'contenttools')
}

[ordered]@{
    version = $Version
    platform = $Platform
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
        & gh release download $StoreTag --repo $Repo --pattern "OpenSCCT-$baseVersion-$Platform-files.json" --dir $baseDir 2>$null
        # Existing launchers know game-files; the first release using a new store still needs its delta base there.
        if ($StoreTag -ne 'game-files' -and -not (Test-Path (Join-Path $baseDir "OpenSCCT-$baseVersion-$Platform-files.json"))) {
            & gh release download game-files --repo $Repo --pattern "OpenSCCT-$baseVersion-$Platform-files.json" --dir $baseDir 2>$null
        }
        if (-not (Test-Path (Join-Path $baseDir "OpenSCCT-$baseVersion-$Platform-files.json"))) {
            & gh release download $BaseTag --repo $Repo --pattern "OpenSCCT-$baseVersion-$Platform-files.json" --dir $baseDir 2>$null
        }
        $baseList = Get-ChildItem -LiteralPath $baseDir -Filter "OpenSCCT-$baseVersion-$Platform-files.json" -ErrorAction SilentlyContinue | Select-Object -First 1
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
$packName = "OpenSCCT-$Version-$Platform-pack.zip"
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
$filesJson = Join-Path $blobDir "OpenSCCT-$Version-$Platform-files.json"
[ordered]@{ format = 1; version = $Version; commit = $commit; files = $entries } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $filesJson -Encoding utf8NoBOM
$newSize = ($uploads | Measure-Object -Property Length -Sum).Sum
Write-Host ('{0} files, {1:N1} MB; {2} new files in {3} uploads ({4:N1} MB compressed)' -f $entries.Count, ($fullSize / 1MB), $newBlobs.Count, $uploads.Count, ($newSize / 1MB))

Remove-Item -LiteralPath $staging -Recurse -Force

if (-not $SkipLauncher) {
    . (Join-Path $PSScriptRoot 'LauncherBuild.ps1')
    $launcher = Resolve-LauncherExe -RepoRoot $repoRoot -Commit $commit -Repo $Repo -LauncherExe $LauncherExe -Platform $Platform
    $launcherVersion = $launcher.Version
    $launcherName = $launcher.VersionedName
    $launcherAsset = Join-Path $OutDir $launcher.ExeName
    Copy-Item -LiteralPath $launcher.Exe -Destination $launcherAsset -Force
    $assets += $launcherAsset
    if ($linux) {
        & python3 (Join-Path $repoRoot 'tools/linux/package-launcher.py') $launcherAsset $OutDir
        if ($LASTEXITCODE -ne 0) { throw 'Packaging the Linux launcher failed.' }
    }
}

foreach ($file in @($filesJson) + $assets) {
    $name = Split-Path $file -Leaf
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    # The launcher's checksum file carries its version (ReleasePicker.LauncherChecksumName); the line names the asset.
    $sumName = if ($file -eq $launcherAsset) { "$launcherName.sha256" } else { "$name.sha256" }
    [System.IO.File]::WriteAllText((Join-Path $blobDir $sumName), "$hash  $name`n")
}

if ($StoreTag -ne 'game-files' -or $linux) {
    # ReleasePicker checks the version's own assets before the legacy game-files store. Keep all three metadata
    # files discoverable there; file-list blob tags already route downloads across storage releases.
    $assets += $filesJson
    $assets += "$filesJson.sha256"
    if ($launcherAsset) { $assets += (Join-Path $blobDir "$launcherName.sha256") }
}
if ($linux -and $launcherAsset) {
    $assets += (Join-Path $OutDir 'OpenSCCT.Launcher-linux-x64.tar.gz')
    $assets += (Join-Path $OutDir 'OpenSCCT.Launcher-linux-x64.tar.gz.sha256')
}

if (-not $NotesFile) {
    $NotesFile = Join-Path $OutDir 'notes.md'
    @"
## OpenSCCT $Version

-

Built from $commit.
"@ | Set-Content -LiteralPath $NotesFile -Encoding utf8NoBOM
}
elseif ([System.IO.Path]::GetFullPath($NotesFile) -ne (Join-Path $OutDir 'notes.md')) {
    Copy-Item -LiteralPath $NotesFile -Destination (Join-Path $OutDir 'notes.md') -Force
    $NotesFile = Join-Path $OutDir 'notes.md'
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
        $relativeAsset = [System.IO.Path]::GetRelativePath($OutDir, $_).Replace('\', '/')
        $portable = '$PSScriptRoot/' + $relativeAsset
        if ($_ -eq $launcherAsset) { '"' + $portable + '#OpenSCCT Launcher ' + $launcherVersion + '"' } else { '"' + $portable + '"' }
    }) -join ' '
$pre = if ($isPrerelease) { ' --prerelease' } else { '' }
$publish = Join-Path $OutDir 'publish.ps1'
$versionContents = if ($StoreTag -eq 'game-files' -and -not $linux) { 'the launcher only' } else { 'the launcher and update metadata' }
@"
# Uploads the game files to the $StoreTag release (made once, as a prerelease so it is never "Latest"), then creates
# the draft release v$Version with $versionContents. Check the draft on GitHub, then publish it.
`$ErrorActionPreference = 'Stop'
# Check before touching the shared store; the same version's second platform attaches to its draft.
gh release view v$Version --repo $Repo *> `$null
if (`$LASTEXITCODE -eq 0) {
    `$draft = gh release view v$Version --repo $Repo --json isDraft --jq '.isDraft'
    if (`$LASTEXITCODE -ne 0 -or `$draft -ne 'true') { throw 'The version release is already published. Prepare both platforms before publishing the draft.' }
}
gh release view $StoreTag --repo $Repo *> `$null
if (`$LASTEXITCODE -ne 0) {
    gh release create $StoreTag --repo $Repo --target $commit --title "Game files (the launcher downloads these)" --prerelease --latest=false --notes "Files the OpenSCCT launcher downloads and checks by itself. You do not need anything here: download the launcher from the latest release instead."
    if (`$LASTEXITCODE -ne 0) { throw 'gh release create $StoreTag failed.' }
}
`$count = [int](gh release view $StoreTag --repo $Repo --json assets --jq '.assets | length')
`$files = @(Get-ChildItem -LiteralPath (Join-Path `$PSScriptRoot 'game-files') -File | ForEach-Object FullName)
if (`$count + `$files.Count -gt 990) { throw "$StoreTag would pass GitHub's 1000 assets per release (`$count there, `$(`$files.Count) new)." }
for (`$i = 0; `$i -lt `$files.Count; `$i += 20) {
    gh release upload $StoreTag @(`$files[`$i..([Math]::Min(`$i + 19, `$files.Count - 1))]) --repo $Repo --clobber
    if (`$LASTEXITCODE -ne 0) { throw 'gh release upload failed; run this script again to retry.' }
}
gh release view v$Version --repo $Repo *> `$null
if (`$LASTEXITCODE -ne 0) {
    gh release create v$Version $quoted --repo $Repo --target $commit --title "OpenSCCT $Version" --notes-file "`$PSScriptRoot/notes.md" --draft$pre
    if (`$LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
}
else {
    # Both platform preparations target the same version. Attach the second set to the existing draft only.
    `$draft = gh release view v$Version --repo $Repo --json isDraft --jq '.isDraft'
    if (`$LASTEXITCODE -ne 0 -or `$draft -ne 'true') { throw 'The version release is already published. Prepare both platforms before publishing the draft.' }
    gh release upload v$Version $quoted --repo $Repo --clobber
    if (`$LASTEXITCODE -ne 0) { throw 'gh release upload of version assets failed.' }
}
"@ | Set-Content -LiteralPath $publish -Encoding utf8NoBOM

Write-Host ''
Write-Host "Edit the notes, then publish with (not run by this script; creates a draft to review first):" -ForegroundColor Yellow
Write-Host "  & ""$publish"""
