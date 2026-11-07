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
$discovery = & dotnet $dll -noAutoReporters -noColor -list full/json
if ($LASTEXITCODE -ne 0) { throw 'Integration test discovery failed.' }
$methods = @($discovery | ConvertFrom-Json | Group-Object { "$($_.Class).$($_.Method)" })
if ($methods.Count -lt 4) { throw 'Integration test discovery returned too few methods.' }

# Prefer measured method times. Keep each theory's cases together and include every newly discovered test.
$profile = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'integration-timings.json') -Raw | ConvertFrom-Json -AsHashtable
$weighted = foreach ($method in $methods) {
    $shortName = $method.Name -replace '^OpenScct\.Integration\.Tests\.', ''
    @{ Name = $method.Name; Weight = [Math]::Max($method.Count, [int]$profile[$shortName]) }
}
$groups = @(1..4 | ForEach-Object { @{ Methods = [Collections.Generic.List[string]]::new(); Weight = 0 } })
foreach ($method in ($weighted | Sort-Object -Property @{ Expression = { $_.Weight }; Descending = $true }, @{ Expression = { $_.Name } })) {
    $group = $groups | Sort-Object -Property { $_.Weight } -Stable | Select-Object -First 1
    $group.Methods.Add($method.Name)
    $group.Weight += $method.Weight
}
$assigned = $groups[$Shard - 1].Methods
if ($Plan) { return $assigned }

Write-Host "Running integration shard $Shard of 4 ($($assigned.Count) methods)."
$results = Join-Path $root 'local/test-results'
New-Item -ItemType Directory -Force $results | Out-Null
$report = Join-Path $results "integration-$Shard.xml"
$filters = foreach ($method in $assigned) { '-method'; $method }
& dotnet $dll -noAutoReporters -noColor -xml $report @filters
if ($LASTEXITCODE -ne 0) { throw "Integration shard $Shard failed (exit $LASTEXITCODE)." }
[xml]$xml = Get-Content -LiteralPath $report -Raw
$ran = @($xml.SelectNodes('//test') | ForEach-Object { "$($_.type).$($_.method)" } | Sort-Object -Unique)
if (Compare-Object -ReferenceObject @($assigned) -DifferenceObject $ran) {
    throw "Integration shard $Shard ran a different set of methods than discovery assigned."
}
