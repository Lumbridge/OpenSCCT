# Testing and CI

[Documentation](README.md) · [Development setup](DEVELOPMENT.md)

Use the checks that exercise your change. A build catches compile errors; simulation tests
check rules; Godot smokes check client integration. Visual, audio and multiplayer changes
also need a real play check.

Run the following commands from the repository root unless noted otherwise.

## Quick checks

```powershell
dotnet build OpenSCCT.slnx -c Release
dotnet test --solution OpenSCCT.slnx -c Release --no-build --ignore-exit-code 8
python tools/ci/docs.py
python tools/ci/no_game_data.py
```

For a focused .NET run, select one project:

```powershell
dotnet test --project tests/OpenScct.Game.Tests
```

The repository uses Microsoft.Testing.Platform. The full CI command permits exit code 8
for a test project with no tests. Do not treat a skipped or empty suite as a passing gameplay check.

## Python tools

```powershell
python -m pip install -r tools/ci/requirements.txt -r content/requirements.txt
python -m pytest tools/ci/tests tests/content -q
```

CI also runs `python -m pytest -q` from `re/tools`, `re/formats` and
`re/ghidra/scripts/transfer`. Those suites use synthetic data. Content tests that need an
original install may skip when it is absent.

## Godot smoke tests

On Windows, this matches the client tests in core CI:

```powershell
python -m pip install -r content/requirements.txt
python -B tools/testmap/generate.py --no-classes --stations-out local/testrange-stations.json
./tools/ci/godot-smoke.ps1 -Tests boot,testbox,range-sim -ExpectedFailures selftest-fail -TimeoutSeconds 180
```

The runner downloads the pinned Godot .NET editor, builds and imports the client, then checks
each test's exit code and result marker. Reports and logs go to `local/test-results/`.
`selftest-fail` must fail in the expected way, proving the runner detects failures.

Tests for converted maps, characters and audio need local content. See
[play testing](TESTING-PLAY.md) for the input harness, windowed checks and smoke selection,
and [TestRange](TESTMAP.md) for the synthetic map.

## What GitHub Actions runs

| Workflow / job | Trigger | Checks and output |
|---|---|---|
| [CI](../.github/workflows/ci.yml): `linux` | Push to `main`, pull request, manual run | Data guard, docs check, vendored-tool check, .NET build/tests, Python tests; uploads `svm-server-linux-x64` |
| CI: `windows` | Same | Data guard, .NET build/tests and banned-API check |
| CI: `godot-smoke` | Same | Generates TestRange; runs `boot`, `testbox`, `range-sim` and the expected failure; uploads `godot-smoke` logs and JUnit report even on failure |
| [Linux export](../.github/workflows/linux-export.yml): `package` | Relevant source, client, packaging or toolchain changes on a PR or `main`; manual run | Native export and Flatpak installation/runtime smokes; uploads `openscct-linux-flatpak` |

Download artifacts from the run's page in [Actions](https://github.com/Lumbridge/OpenSCCT/actions).
Core CI artifacts are kept for seven days. Linux export does not run for a docs-only change.
The determinism-hash steps in core CI are placeholders; they do not yet compare Windows
and Linux replay hashes.

## What coverage means here

The [coverage report](progress/COVERAGE.md) tracks original-game units through specification,
porting and recorded comparison checks. It is separate from the automated test suites.
CI does not currently publish C# line or branch coverage.

The README shows a dated snapshot from the checked-in report. To refresh it after the relevant
code and comparison evidence are ready:

```powershell
dotnet build OpenSCCT.slnx
dotnet build game/OpenScctGame.slnx
dotnet run --project tools/OpenScct.Coverage
python tools/ci/docs.py --write
```

Review the report's inputs and warnings before committing it. Verified counts depend on the
local comparison report; without that input they can fall. Do not regenerate a report just
to make a badge look newer. The [coverage tool guide](../tools/OpenScct.Coverage/README.md)
defines the rules and output files.

`python tools/ci/docs.py` checks that the README table matches the report and that repository
Markdown links and heading anchors resolve. It does not check external websites or prove
that the coverage report reflects the latest source.

## Before calling a change tested

Record the commands, results and build you checked. For gameplay changes, include the map,
side, host/client role and reproduction steps. Note skipped tests and remaining checks.

Use [two-PC testing](NETWORK-TEST.md) or the [network lab](NETWORK-LAB.md) for networking work.
A headless smoke cannot establish visual quality, audio quality, hardware compatibility or
full parity with the original game.
