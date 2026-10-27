<#
.SYNOPSIS
    Proves the banned-API analyzer works: builds a throwaway sim library that calls MathF.Sin and expects the
    build to fail with RS0030.

.DESCRIPTION
    Creates a project under the temp folder that picks up the repository's Directory.Build.props,
    Directory.Packages.props and build/Sim.props, exactly like a real sim library. Exits 0 only when the build
    fails and reports RS0030 for MathF.Sin; prints the diagnostics either way.
#>
[CmdletBinding()]
param(
    [switch]$KeepTemp
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('svm-banned-api-' + [System.Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $repoBuildProps = Join-Path $repoRoot 'Directory.Build.props'
    $repoPackagesProps = Join-Path $repoRoot 'Directory.Packages.props'

    Set-Content -LiteralPath (Join-Path $work 'Directory.Build.props') -Encoding utf8 -Value @"
<Project>
  <Import Project="$repoBuildProps" />
</Project>
"@
    Set-Content -LiteralPath (Join-Path $work 'Directory.Packages.props') -Encoding utf8 -Value @"
<Project>
  <Import Project="$repoPackagesProps" />
</Project>
"@
    Copy-Item -LiteralPath (Join-Path $repoRoot 'global.json') -Destination $work

    Set-Content -LiteralPath (Join-Path $work 'BannedApiProbe.csproj') -Encoding utf8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="$(RepoRoot)build/Sim.props" />
</Project>
'@
    Set-Content -LiteralPath (Join-Path $work 'Probe.cs') -Encoding utf8 -Value @'
namespace BannedApiProbe;

public static class Probe
{
    public static float Run(float x) => System.MathF.Sin(x);
}
'@

    Write-Host "Building probe in $work"
    Push-Location $work
    try {
        $output = & dotnet build 'BannedApiProbe.csproj' -nologo 2>&1 | ForEach-Object { "$_" }
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $diagnostics = @($output | Where-Object { $_ -match 'RS0030' } | Sort-Object -Unique)
    foreach ($line in $diagnostics) {
        Write-Host $line
    }

    $sinFlagged = @($diagnostics | Where-Object { $_ -match 'MathF\.Sin' }).Count -gt 0
    if ($exitCode -ne 0 -and $sinFlagged) {
        Write-Host 'OK: the build failed with RS0030 for MathF.Sin, so banned APIs are enforced.'
        exit 0
    }

    if ($exitCode -eq 0) {
        Write-Host 'FAIL: the probe built successfully; the banned-API analyzer is not enforced.'
    }
    else {
        Write-Host "FAIL: the build failed (exit $exitCode) but not with RS0030 for MathF.Sin. Build output:"
        $output | ForEach-Object { Write-Host $_ }
    }

    exit 1
}
finally {
    if (-not $KeepTemp) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
