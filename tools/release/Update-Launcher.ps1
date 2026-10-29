<#
.SYNOPSIS
    Ships a new launcher on the latest version release, without a new game version.

.DESCRIPTION
    Players' launchers read the latest version release (v<version>) for the game and for the launcher: the launcher's
    version is the label of its exe ("OpenSCCT Launcher <version>"). A launcher-only change therefore does not need a
    game version (which makes players download the game again and prepare their game files again): this script swaps
    the launcher exe of an existing release instead.

    It builds the launcher of LauncherVersion in Directory.Build.props (or reuses the kept copy, LauncherBuild.ps1),
    then:
        1. uploads OpenSCCT.Launcher-<launcher version>-win-x64.exe.sha256 to the game files release (first, so a
           launcher that sees the new label always finds its checksum),
        2. replaces OpenSCCT.Launcher.exe on the version release, labelled with the new launcher version,
        3. adds a "Launcher <version>" section to the release notes.

    Run it from a clean checkout whose HEAD is pushed. Bump LauncherVersion first, never VersionPrefix.

.EXAMPLE
    tools\release\Update-Launcher.ps1 -Notes "- **Use downloads.** Step 1 has a button to stop using your game folder."
#>
#Requires -Version 7
[CmdletBinding()]
param(
    # Release to update (default: the newest published version release).
    [string]$Tag,

    # Markdown for the release notes' new section (what changed in the launcher).
    [Parameter(Mandatory = $true)]
    [string]$Notes,

    [string]$Repo = 'Lumbridge/OpenSCCT',

    # The release that holds every version's game files (ReleasePicker.StoreTag in the launcher).
    [string]$StoreTag = 'game-files'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if (& git -C $repoRoot status --porcelain) { throw 'The checkout has uncommitted changes: commit and push them first.' }
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
& git -C $repoRoot fetch -q origin
& git -C $repoRoot merge-base --is-ancestor $commit origin/main
if ($LASTEXITCODE -ne 0) { throw "HEAD ($commit) is not on origin/main: push it first." }

if (-not $Tag) {
    $Tag = (& gh release list --repo $Repo --exclude-drafts --json tagName,isLatest --jq '.[] | select(.isLatest) | .tagName').Trim()
    if (-not $Tag) { throw 'No latest release found.' }
}
$release = & gh release view $Tag --repo $Repo --json assets,body | ConvertFrom-Json
$current = $release.assets | Where-Object { $_.name -eq 'OpenSCCT.Launcher.exe' } | Select-Object -First 1
if (-not $current) { throw "$Tag has no OpenSCCT.Launcher.exe." }
$label = 'OpenSCCT Launcher '
if (-not $current.label.StartsWith($label)) { throw "The launcher of $Tag is labelled '$($current.label)', not '$label<version>'." }
$released = $current.label.Substring($label.Length).Trim()

. (Join-Path $PSScriptRoot 'LauncherBuild.ps1')
$launcher = Resolve-LauncherExe -RepoRoot $repoRoot -Commit $commit -Repo $Repo
if ([version]($launcher.Version -replace '-.*$', '') -le [version]($released -replace '-.*$', '')) {
    throw "Launcher $($launcher.Version) is not newer than the $released on $Tag. Bump LauncherVersion in Directory.Build.props."
}

$outDir = Join-Path $repoRoot "local\release\launcher-$($launcher.Version)"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$asset = Join-Path $outDir $launcher.ExeName
Copy-Item -LiteralPath $launcher.Exe -Destination $asset -Force
$hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
$sum = Join-Path $outDir "$($launcher.VersionedName).sha256"
[System.IO.File]::WriteAllText($sum, "$hash  $($launcher.ExeName)`n")

Write-Host "Launcher $released -> $($launcher.Version) on $Tag (from $($commit.Substring(0, 7)))."
& gh release upload $StoreTag $sum --repo $Repo --clobber
if ($LASTEXITCODE -ne 0) { throw "Uploading the checksum to $StoreTag failed." }
& gh release upload $Tag "$asset#$label$($launcher.Version)" --repo $Repo --clobber
if ($LASTEXITCODE -ne 0) { throw "Replacing the launcher on $Tag failed." }

$body = ($release.body ?? '').TrimEnd()
$section = "### Launcher $($launcher.Version)`n`n$($Notes.Trim())`n`nLauncher built from $($commit.Substring(0, 7))."
# The players' line ("the launcher updates itself to X") names the new launcher.
$body = $body -replace [regex]::Escape("updates itself to $released"), "updates itself to $($launcher.Version)"
$notesFile = Join-Path $outDir 'notes.md'
[System.IO.File]::WriteAllText($notesFile, "$body`n`n$section`n")
& gh release edit $Tag --repo $Repo --notes-file $notesFile | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Updating the notes of $Tag failed (the launcher is already replaced)." }

$check = (& gh release view $Tag --repo $Repo --json assets --jq '.assets[] | select(.name == "OpenSCCT.Launcher.exe") | .label').Trim()
Write-Host "Done: $Tag now carries '$check'." -ForegroundColor Green
