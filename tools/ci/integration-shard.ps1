#Requires -Version 7
param(
    [Parameter(Mandatory)]
    [ValidateRange(1, 4)]
    [int] $Shard,
    [switch] $Plan
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$dll = Join-Path $root 'tests/OpenScct.Integration.Tests/bin/Release/net10.0/OpenScct.Integration.Tests.dll'
$discovery = & dotnet $dll -list full/json
if ($LASTEXITCODE -ne 0) { throw 'Integration test discovery failed.' }
$classes = @($discovery | ConvertFrom-Json | Group-Object Class)
if ($classes.Count -lt 4) { throw 'Integration test discovery returned too few classes.' }

# Prefer measured class times. Every new class is included using its discovered test count.
$profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'integration-timings.json') -Raw | ConvertFrom-Json -AsHashtable
$weighted = foreach ($class in $classes) {
    $shortName = $class.Name -replace '^OpenScct\.Integration\.Tests\.', ''
    @{ Name = $class.Name; Weight = [Math]::Max($class.Count, [int]$profile[$shortName]) }
}
$groups = @(1..4 | ForEach-Object { @{ Classes = [Collections.Generic.List[string]]::new(); Weight = 0 } })
foreach ($class in ($weighted | Sort-Object -Property @{ Expression = { $_.Weight }; Descending = $true }, @{ Expression = { $_.Name } })) {
    $group = $groups | Sort-Object -Property { $_.Weight } -Stable | Select-Object -First 1
    $group.Classes.Add($class.Name)
    $group.Weight += $class.Weight
}
$assigned = $groups[$Shard - 1].Classes
if ($Plan) { return $assigned }

Write-Host "Running integration shard $Shard of 4 ($($assigned.Count) classes)."
$results = Join-Path $root 'local/test-results'
New-Item -ItemType Directory -Force $results | Out-Null
$report = Join-Path $results "integration-$Shard.xml"
$filters = foreach ($class in $assigned) { '-class'; $class }
& dotnet $dll -noAutoReporters -noColor -xml $report @filters
if ($LASTEXITCODE -ne 0) { throw "Integration shard $Shard failed (exit $LASTEXITCODE)." }
[xml]$xml = Get-Content -LiteralPath $report -Raw
$ran = @($xml.SelectNodes('//test') | ForEach-Object { $_.GetAttribute('type') } | Sort-Object -Unique)
if (Compare-Object -ReferenceObject @($assigned) -DifferenceObject $ran) {
    throw "Integration shard $Shard ran a different set of classes than discovery assigned."
}
