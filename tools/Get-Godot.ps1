<#
.SYNOPSIS
    Downloads the pinned Godot .NET editor (and optionally its export templates) and prints the console exe path.

.DESCRIPTION
    Reads the version from godot-version.txt at the repo root (for example 4.7.2-stable), downloads
    Godot_v<version>_mono_win64.zip and SHA512-SUMS.txt from the official GitHub release, checks the
    SHA-512 and extracts the editor to tools/godot/<version>/. Safe to run again: a verified install is
    left alone.

    With -ExportTemplates it also fetches Godot_v<version>_mono_export_templates.tpz, checks its SHA-512
    and installs it to %APPDATA%\Godot\export_templates\<major.minor.patch>.<status>.mono, which is where
    a non self-contained editor looks for templates.

    The last line written to the output stream is the full path of the console exe, so callers can do
    $godot = & tools/Get-Godot.ps1

.PARAMETER ExportTemplates
    Also download and install the matching .NET export templates.

.PARAMETER Force
    Download and extract again even if a verified install is present.

.PARAMETER SourceZip
    Use an already downloaded editor zip instead of downloading it. It is still checked against SHA512-SUMS.txt.
#>
[CmdletBinding()]
param(
    [switch]$ExportTemplates,
    [switch]$Force,
    [string]$SourceZip
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
# The progress bar makes Invoke-WebRequest very slow on Windows PowerShell 5.1.
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $repoRoot 'godot-version.txt'
$version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($version -notmatch '^(\d+)\.(\d+)(?:\.(\d+))?-([a-z0-9]+)$') {
    throw "Unexpected Godot version '$version' in $versionFile (expected something like 4.7.2-stable)."
}
# Templates live in a folder named like 4.7.2.stable.mono (patch is omitted from the tag when it is 0).
$patch = if ($Matches[3]) { $Matches[3] } else { '0' }
$templateDirName = "$($Matches[1]).$($Matches[2]).$patch.$($Matches[4]).mono"

$releaseBase = "https://github.com/godotengine/godot/releases/download/$version"
$editorZipName = "Godot_v${version}_mono_win64.zip"
$templatesName = "Godot_v${version}_mono_export_templates.tpz"
$consoleExeName = "Godot_v${version}_mono_win64_console.exe"

$godotRoot = Join-Path $repoRoot 'tools/godot'
$downloadDir = Join-Path $godotRoot "downloads/$version"
$installDir = Join-Path $godotRoot $version
$consoleExe = Join-Path $installDir $consoleExeName
$editorMarker = Join-Path $installDir '.svm-verified'

function Write-Step([string]$Message) {
    Write-Host "[get-godot] $Message"
}

function Get-RemoteFile([string]$Url, [string]$Destination) {
    $partial = "$Destination.partial"
    if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force }
    Write-Step "downloading $Url"
    Invoke-WebRequest -Uri $Url -OutFile $partial -UseBasicParsing
    Move-Item -LiteralPath $partial -Destination $Destination -Force
}

function Get-ExpectedHash([string]$SumsPath, [string]$FileName) {
    foreach ($line in Get-Content -LiteralPath $SumsPath) {
        $parts = $line.Trim() -split '\s+', 2
        if ($parts.Count -eq 2 -and $parts[1].TrimStart('*') -eq $FileName) {
            return $parts[0].ToLowerInvariant()
        }
    }
    throw "No SHA-512 entry for $FileName in $SumsPath."
}

function Assert-Hash([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) {
        throw "SHA-512 mismatch for ${Path}:`n  expected $Expected`n  actual   $actual"
    }
    Write-Step "SHA-512 ok for $(Split-Path -Leaf $Path)"
}

# Downloads a release asset unless a copy with the right hash is already there.
function Get-VerifiedAsset([string]$FileName, [string]$Expected, [string]$LocalSource) {
    $path = Join-Path $downloadDir $FileName
    if ($LocalSource) {
        Assert-Hash $LocalSource $Expected
        Copy-Item -LiteralPath $LocalSource -Destination $path -Force
        return $path
    }
    if ((Test-Path -LiteralPath $path) -and -not $Force) {
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA512).Hash.ToLowerInvariant()
        if ($actual -eq $Expected) {
            Write-Step "using cached $FileName"
            return $path
        }
        Write-Step "cached $FileName has the wrong hash, downloading again"
    }
    Get-RemoteFile "$releaseBase/$FileName" $path
    Assert-Hash $path $Expected
    return $path
}

New-Item -ItemType Directory -Force -Path $downloadDir | Out-Null

$sumsPath = Join-Path $downloadDir 'SHA512-SUMS.txt'
if (-not (Test-Path -LiteralPath $sumsPath) -or $Force) {
    Get-RemoteFile "$releaseBase/SHA512-SUMS.txt" $sumsPath
}

# Editor
$editorHash = Get-ExpectedHash $sumsPath $editorZipName
$installed = (Test-Path -LiteralPath $consoleExe) -and (Test-Path -LiteralPath $editorMarker) -and
    ((Get-Content -LiteralPath $editorMarker -Raw).Trim() -eq $editorHash)
if ($installed -and -not $Force) {
    Write-Step "Godot $version already installed in $installDir"
}
else {
    $zipPath = Get-VerifiedAsset $editorZipName $editorHash $SourceZip
    $staging = Join-Path $godotRoot "staging-$version"
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    Write-Step "extracting $editorZipName"
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $staging)
    # The zip holds a single top folder (Godot_v<version>_mono_win64); install its contents directly.
    $top = @(Get-ChildItem -LiteralPath $staging)
    $content = if ($top.Count -eq 1 -and $top[0].PSIsContainer) { $top[0].FullName } else { $staging }
    if (-not (Test-Path -LiteralPath (Join-Path $content $consoleExeName))) {
        throw "$consoleExeName not found in $editorZipName."
    }
    if (Test-Path -LiteralPath $installDir) { Remove-Item -LiteralPath $installDir -Recurse -Force }
    Move-Item -LiteralPath $content -Destination $installDir
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    Set-Content -LiteralPath $editorMarker -Value $editorHash -NoNewline
    Write-Step "installed Godot $version to $installDir"
}

# Export templates
if ($ExportTemplates) {
    $templatesHash = Get-ExpectedHash $sumsPath $templatesName
    $templatesRoot = Join-Path $env:APPDATA 'Godot/export_templates'
    $templatesDir = Join-Path $templatesRoot $templateDirName
    $templatesMarker = Join-Path $templatesDir '.svm-verified'
    $haveTemplates = (Test-Path -LiteralPath $templatesMarker) -and
        ((Get-Content -LiteralPath $templatesMarker -Raw).Trim() -eq $templatesHash)
    if ($haveTemplates -and -not $Force) {
        Write-Step "export templates already installed in $templatesDir"
    }
    else {
        $tpzPath = Get-VerifiedAsset $templatesName $templatesHash $null
        $staging = Join-Path $godotRoot "staging-templates-$version"
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
        Write-Step "extracting $templatesName"
        # A .tpz is a zip with a single templates/ folder and a version.txt naming the target folder.
        [System.IO.Compression.ZipFile]::ExtractToDirectory($tpzPath, $staging)
        $inner = Join-Path $staging 'templates'
        $versionTxt = Join-Path $inner 'version.txt'
        if (-not (Test-Path -LiteralPath $versionTxt)) { throw "$templatesName has no templates/version.txt." }
        $tplVersion = (Get-Content -LiteralPath $versionTxt -Raw).Trim()
        if ($tplVersion -ne $templateDirName) {
            throw "$templatesName is for '$tplVersion', expected '$templateDirName'."
        }
        New-Item -ItemType Directory -Force -Path $templatesRoot | Out-Null
        if (Test-Path -LiteralPath $templatesDir) { Remove-Item -LiteralPath $templatesDir -Recurse -Force }
        Move-Item -LiteralPath $inner -Destination $templatesDir
        Remove-Item -LiteralPath $staging -Recurse -Force
        Set-Content -LiteralPath $templatesMarker -Value $templatesHash -NoNewline
        Write-Step "installed export templates to $templatesDir"
    }
}

Write-Output $consoleExe
