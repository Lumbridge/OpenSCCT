<#
.SYNOPSIS
    Packs the content builder (svmbuild) with its own Python so an installed game can build the player's content
    from their Splinter Cell Chaos Theory install. New-Release.ps1 runs it into the build's contenttools\ folder.

.DESCRIPTION
    Writes <OutDir>:
        python\     the CPython embeddable package (python.org) with numpy, Pillow and jsonschema (PyPI wheels, the
                    versions of content/requirements.txt) in Lib\site-packages
        repo\       our tracked sources the builder runs from, in the repository's layout (svmbuild finds the rest
                    from its own path): content\svmbuild, tools\scctpkg\scctpkg, re\formats\scctformats, spec\svmap

    Only git-tracked files are copied, so game-derived files (tools\scctpkg\scctpkg\class_defaults.json, made on
    the player's machine from their install by the classes step) and caches never ship.

    The Python package and the wheels are downloaded once into local\release-cache\contenttools and reused.

    The launcher runs: contenttools\python\python.exe -u -m svmbuild <steps> --game <install> --out <content> ...
    (docs/LAUNCHER.md, "Game content").
#>
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutDir,

    # Python of the embeddable package; the pipeline is pinned to 3.10 (content/requirements.txt).
    [string]$PythonVersion = '3.10.11'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cache = Join-Path $repoRoot 'local\release-cache\contenttools'
New-Item -ItemType Directory -Force $cache | Out-Null

if (Test-Path $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
$python = Join-Path $OutDir 'python'
$repo = Join-Path $OutDir 'repo'
New-Item -ItemType Directory -Force $python, $repo | Out-Null

# The embeddable Python.
$embedName = "python-$PythonVersion-embed-amd64.zip"
$embedZip = Join-Path $cache $embedName
if (-not (Test-Path $embedZip)) {
    Write-Host "Downloading $embedName from python.org"
    Invoke-WebRequest -Uri "https://www.python.org/ftp/python/$PythonVersion/$embedName" -OutFile "$embedZip.partial"
    Move-Item -LiteralPath "$embedZip.partial" -Destination $embedZip
}
Write-Host ("{0}  sha256 {1}" -f $embedName, (Get-FileHash -LiteralPath $embedZip -Algorithm SHA256).Hash.ToLowerInvariant())
Expand-Archive -LiteralPath $embedZip -DestinationPath $python

# The wheels, for the embedded Python's version and platform (the local pip only downloads and unpacks them).
$tag = ($PythonVersion -split '\.')[0..1] -join ''
$requirements = Get-Content (Join-Path $repoRoot 'content\requirements.txt') |
    Where-Object { $_ -match '^[A-Za-z]' -and $_ -notmatch '^pytest' }
$site = Join-Path $python 'Lib\site-packages'
& python -m pip install --disable-pip-version-check --no-compile --target $site --platform win_amd64 `
    --python-version ($PythonVersion -replace '^(\d+\.\d+).*', '$1') --implementation cp --only-binary=:all: `
    --cache-dir (Join-Path $cache 'pip') @requirements
if ($LASTEXITCODE -ne 0) { throw 'pip could not fetch the content builder wheels.' }
Get-ChildItem -LiteralPath $site -Directory -Filter '__pycache__' -Recurse | Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $site -Directory -Filter 'bin' | Remove-Item -Recurse -Force

# The embedded Python reads its search path from python3XX._pth (PYTHONPATH is ignored): the standard library,
# the wheels and our sources.
$pth = Join-Path $python "python$tag._pth"
@(
    "python$tag.zip"
    '.'
    'Lib\site-packages'
    '..\repo\content'
    'import site'
) | Set-Content -LiteralPath $pth -Encoding ascii

# Our sources, tracked files only.
$trees = @('content/svmbuild', 'content/assets/sidearms/export', 'content/assets/sidearms/CREDITS.txt', 'tools/scctpkg/scctpkg', 're/formats/scctformats', 'spec/svmap')
$files = & git -C $repoRoot ls-files -- @trees
if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
foreach ($file in $files) {
    $src = Join-Path $repoRoot $file
    if (-not (Test-Path -LiteralPath $src)) { continue }
    $dest = Join-Path $repo $file
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dest
}
if (Test-Path (Join-Path $repo 'tools\scctpkg\scctpkg\class_defaults.json')) {
    throw 'class_defaults.json is game-derived and must not ship.'
}

# A quick check that the package imports (no game files needed).
$exe = Join-Path $python 'python.exe'
& $exe -c "import numpy, PIL, jsonschema, svmbuild.cli, svmbuild.steps; print('content builder ok')"
if ($LASTEXITCODE -ne 0) { throw 'The packed content builder does not import.' }

$size = (Get-ChildItem -LiteralPath $OutDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ('Content builder in {0} ({1:N1} MB)' -f $OutDir, ($size / 1MB))
