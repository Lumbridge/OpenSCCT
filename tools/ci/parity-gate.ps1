param(
    [string]$Configuration = 'Debug',
    [string]$ArtifactsPath = 'local/parity-gate/artifacts',
    [string]$GoldenRoot = 'local/difftest/golden'
)

# A local evidence gate: reference recordings and converted content must exist, and every movement frame/repeat
# must match. The xUnit console's failSkips also makes missing optional content fail this gate, while ordinary
# source-only CI continues to report those fixtures as explicit skips.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$previousRequirement = [Environment]::GetEnvironmentVariable('OPENSCCT_REQUIRE_GOLDENS')
$previousRoot = [Environment]::GetEnvironmentVariable('OPENSCCT_GOLDEN_ROOT')
Push-Location $repo
try {
    $env:OPENSCCT_REQUIRE_GOLDENS = '1'
    $env:OPENSCCT_GOLDEN_ROOT = [System.IO.Path]::GetFullPath($GoldenRoot)
    $artifacts = [System.IO.Path]::GetFullPath($ArtifactsPath)
    $configFolder = $Configuration.ToLowerInvariant()
    foreach ($project in @('OpenScct.Game.Tests', 'OpenScct.Integration.Tests')) {
        & dotnet build "tests/$project/$project.csproj" --configuration $Configuration `
            --artifacts-path $artifacts --nologo -v minimal
        if ($LASTEXITCODE -ne 0) { throw "$project parity build failed (exit $LASTEXITCODE)." }
        $assembly = Join-Path $artifacts "bin/$project/$configFolder/$project.dll"
        $classes = if ($project -eq 'OpenScct.Game.Tests') {
            @('-class', 'OpenScct.Game.Tests.Movement.*', '-class', 'OpenScct.Game.Tests.Animation.ActionClipTests')
        } else {
            @('-class', 'OpenScct.Integration.Tests.MapInteractionParityTests', '-class', 'OpenScct.Integration.Tests.MapLiftParityTests',
              '-class', 'OpenScct.Integration.Tests.RollPoseContentTests', '-class', 'OpenScct.Integration.Tests.RollLaserTests')
        }
        & dotnet $assembly -noLogo -noColor -failSkips @classes -result-junit (Join-Path $artifacts "$project.parity.xml")
        if ($LASTEXITCODE -ne 0) { throw "$project parity gate failed (exit $LASTEXITCODE)." }
    }
}
finally {
    [Environment]::SetEnvironmentVariable('OPENSCCT_REQUIRE_GOLDENS', $previousRequirement)
    [Environment]::SetEnvironmentVariable('OPENSCCT_GOLDEN_ROOT', $previousRoot)
    Pop-Location
}
