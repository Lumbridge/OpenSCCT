<#
.SYNOPSIS
    Packs an exported OpenSCCT build (and optionally the launcher) into release assets with a checksum file, and
    prints the gh command that would publish them. Publishes nothing itself.

.DESCRIPTION
    Writes to local\release\<version>\ (gitignored):
        OpenSCCT-<version>-win-x64.zip            the exported game build, our code only
        OpenSCCT.Launcher-<version>-win-x64.exe   the launcher (when found or published with -PublishLauncher)
        checksums.txt                             SHA-256 of each asset, sha256sum format
        notes.md                                  release notes template (when -NotesFile is not given)

    The build is checked for original game files and converted content before it is zipped: a release carries our
    code builds only. Game content is built on the player's machine from their own install.

    The launcher (src/OpenScct.Launcher.Core) reads these exact names; see docs/LAUNCHER.md.

.EXAMPLE
    tools\release\New-Release.ps1 -Version 0.2.0
.EXAMPLE
    tools\release\New-Release.ps1 -Version 0.3.0-beta.1 -BuildDir local\testbuild -PublishLauncher
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # Folder with the exported game (OpenSCCT.exe and its data folder).
    [string]$BuildDir,

    # Launcher exe to attach. Default: the dotnet publish output, if it exists.
    [string]$LauncherExe,

    # Run dotnet publish for the launcher with this version first.
    [switch]$PublishLauncher,

    # Leave the launcher out of the release.
    [switch]$SkipLauncher,

    [string]$OutDir,

    [string]$Repo = 'Lumbridge/OpenSCCT',

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

# The file list for delta updates (docs/LAUNCHER.md "Delta updates"): every file with its SHA-256 and the release
# asset that holds it gzipped. Files the base release already has keep pointing at their old asset; only the
# others become new blobs of this release.
$known = @{}
if (-not $NoDelta) {
    if (-not $BaseTag) {
        $listed = & gh release list --repo $Repo --exclude-drafts --limit 30 --json tagName,publishedAt 2>$null | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { Write-Warning 'gh could not list releases; every file becomes a new blob.'; $listed = @() }
        $BaseTag = ($listed | Sort-Object publishedAt -Descending | Select-Object -First 1).tagName
    }
    if ($BaseTag) {
        $baseDir = Join-Path $OutDir '.base'
        & gh release download $BaseTag --repo $Repo --pattern '*-files.json' --dir $baseDir 2>$null
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
$blobDir = Join-Path $OutDir 'blobs'
New-Item -ItemType Directory -Force $blobDir | Out-Null
$packLimit = 256KB
$packName = "OpenSCCT-$Version-win-x64-pack.zip"
$packPath = Join-Path $blobDir $packName
$pack = $null
$packRef = [ordered]@{ tag = "v$Version"; name = $packName; size = 0 }
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
                $blob = [ordered]@{ tag = "v$Version"; name = "$sha.gz"; size = (Get-Item -LiteralPath $gz).Length }
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
$filesJson = Join-Path $OutDir "OpenSCCT-$Version-win-x64-files.json"
[ordered]@{ format = 1; version = $Version; commit = $commit; files = $entries } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $filesJson -Encoding utf8NoBOM
$assets += $filesJson
$uploads = @(Get-ChildItem -LiteralPath $blobDir -File)
$newSize = ($uploads | Measure-Object -Property Length -Sum).Sum
Write-Host ('{0} files, {1:N1} MB; {2} new files in {3} uploads ({4:N1} MB compressed)' -f $entries.Count, ($fullSize / 1MB), $newBlobs.Count, $uploads.Count, ($newSize / 1MB))

$gameZip = Join-Path $OutDir "OpenSCCT-$Version-win-x64.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $gameZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -LiteralPath $staging -Recurse -Force
$assets += $gameZip

if (-not $SkipLauncher) {
    $project = Join-Path $repoRoot 'src\OpenScct.Launcher\OpenScct.Launcher.csproj'
    $publishDir = Join-Path $repoRoot 'src\OpenScct.Launcher\bin\Release\net10.0-windows\win-x64\publish'
    if ($PublishLauncher) {
        & dotnet publish $project -c Release -r win-x64 "-p:Version=$Version" -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish of the launcher failed.' }
    }
    if (-not $LauncherExe) { $LauncherExe = Join-Path $publishDir 'OpenSCCT.Launcher.exe' }
    if (Test-Path $LauncherExe) {
        $launcherAsset = Join-Path $OutDir "OpenSCCT.Launcher-$Version-win-x64.exe"
        Copy-Item -LiteralPath $LauncherExe -Destination $launcherAsset
        $assets += $launcherAsset
        $stamped = (Get-Item $LauncherExe).VersionInfo.ProductVersion
        if ($stamped -and -not $stamped.StartsWith($Version)) {
            Write-Warning "The launcher exe says version '$stamped', not $Version. Publish it with -PublishLauncher so it does not offer itself as an update forever."
        }
    }
    else {
        Write-Warning "No launcher exe at $LauncherExe; the release will not update launchers. Use -PublishLauncher or -SkipLauncher."
    }
}

$lines = foreach ($asset in $assets) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $asset -Leaf)"
}
$checksums = Join-Path $OutDir 'checksums.txt'
[System.IO.File]::WriteAllText($checksums, (($lines -join "`n") + "`n"))
$assets += $checksums

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
    Write-Host "  $size  $(Split-Path $asset -Leaf)"
}
Write-Host ("  {0,10:N1} MB  {1} new file uploads in blobs\" -f ($newSize / 1MB), $uploads.Count)

# The blobs are too many for one command line, so the publish steps go in a script to review and run.
$quoted = ($assets | ForEach-Object { '"' + $_ + '"' }) -join ' '
$pre = if ($isPrerelease) { ' --prerelease' } else { '' }
$publish = Join-Path $OutDir 'publish.ps1'
@"
# Creates the draft release v$Version with its assets and new blobs. Check the draft on GitHub, then publish it.
`$ErrorActionPreference = 'Stop'
gh release view v$Version --repo $Repo *> `$null
if (`$LASTEXITCODE -ne 0) {
    gh release create v$Version $quoted --repo $Repo --target $commit --title "OpenSCCT $Version" --notes-file "$NotesFile" --draft$pre
    if (`$LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
}
`$blobs = @(Get-ChildItem -LiteralPath "$blobDir" -File | ForEach-Object FullName)
for (`$i = 0; `$i -lt `$blobs.Count; `$i += 20) {
    gh release upload v$Version @(`$blobs[`$i..([Math]::Min(`$i + 19, `$blobs.Count - 1))]) --repo $Repo --clobber
    if (`$LASTEXITCODE -ne 0) { throw 'gh release upload failed; run this script again to retry the blobs.' }
}
"@ | Set-Content -LiteralPath $publish -Encoding utf8NoBOM

Write-Host ''
Write-Host "Edit the notes, then publish with (not run by this script; creates a draft to review first):" -ForegroundColor Yellow
Write-Host "  & ""$publish"""
