# Behaviour coverage report

`OpenScct.Coverage` measures how much of the original game's recorded behaviour has a spec,
a C# port or a passing differential test. **It does not measure C# line or branch coverage.**
The [published report](../../docs/progress/COVERAGE.md) includes a full breakdown and evidence.

## Run

Run from the repository root. Build first: the tool reads provenance attributes from compiled
assemblies, not source files.

```powershell
dotnet build OpenSCCT.slnx
dotnet build game/OpenScctGame.slnx
dotnet run --project tools/OpenScct.Coverage
dotnet run --project tools/OpenScct.Coverage -- --check
```

The first run writes the reports; `--check` validates without writing. Add `--strict` to fail on
warnings too. Use `--help` for all options, including assembly selection and output paths.

The tool works without game data. Verified status normally needs a local simulation-runner
report. `--no-sim-report` ignores that report; a fresh checkout will not reproduce verification
figures that depend on local goldens. Refresh the reports only from the intended build and
test inputs.

## Inputs

| Input | Path | Gives |
|---|---|---|
| Inventory | `re/inventory/inventory.json` | every unit, its kind, class, subsystem guess, slice flag and bytecode size |
| Assemblies | `src/*/bin/<newest or -c config>/net10.0/<Project>.dll`, `game/.godot/mono/temp/bin/**/OpenScctGame.dll` | `[PortOf]`, `[Uc]` and `[UcNative]` attributes, and the generated class skeletons (types with a nested `UcMeta`), read as metadata with System.Reflection.Metadata, never loaded |
| Status file | `coverage/status.yaml` | explicit unit statuses and out-of-scope (n-a) globs with reasons |
| Specs | `spec/**/*.md` | the `Units:` lists: a line starting with `Units:`, inline or as bullets, up to the next blank line or heading; brace groups `State.{A, B}` expand, notes in parentheses are ignored |
| Scenarios | `tests/scenarios/**/*.scenario.json` | the `covers` list of each scenario |
| Runner report | `local/difftest/sim/report.json` (or `$SVM_DIFFTEST_SIM_OUT`) | whether each scenario's golden passes |

## Statuses

Each unit gets the highest status it earns; each implies the ones before it.

| Status | Rule |
|---|---|
| unported | the default |
| specced | listed under a spec's `Units:`, or `status: specced` in the status file |
| ported | a `[PortOf]` with its id; a `[Uc]` path that resolves to it (`Pkg.Class[.State].Function`, `Pkg.Class.State@code`; a path naming a class or a state is a marker with no unit); a `[UcNative]` exec thunk (`AActor::execPlayAnim` is `nat:Engine.Actor.PlayAnim`); for `def:` units also the class skeleton generator having emitted the class with its decoded defaults (`docs/CODEGEN.md`) |
| verified | ported, and covered by a scenario whose golden passes in the runner report: the `DtReplay` rows must pass (`--all-timings`: every row); or `status: verified` with `evidence` in the status file |
| n-a | matched by an n-a glob or `status: n-a` in the status file. A unit with a hand-written port keeps its status and is reported; generated defaults alone do not count against n-a |

`[UcNative]` names that are not exec thunks, and `[PortOf]`/spec ids of kind `eng:`, `fmt:` and `rl:`,
are units outside the inventory. They are counted separately (there is no full list of them yet).

## Weights

The weight of a unit is its in-memory bytecode size (`script_size`). Default properties and
replication members have no bytecode, so they weigh 0 and show in the unit columns only. Every
table gives unit counts with their share of in-scope units, and the weighted shares of in-scope bytes.

## Errors and warnings

Errors (`--check` fails): a `[PortOf]`, `[Uc]`, scenario `covers` or status file id of kind `uc:`,
`nat:`, `rep:` or `def:` that is not in the inventory, a `[PortOf]` spec file that does not exist, an
n-a entry without a reason, a malformed status file.

Warnings: a `[PortOf]` without a spec anchor or with an anchor that names no heading, a `[Uc]` SHA-1
that differs from the inventory's (the original changed since the port), a `nat:` id written in C++
form, an n-a glob that matches nothing, an n-a unit that is ported, a unit covered by a passing scenario
but not ported, an assembly older than its sources. Spec `Units:` ids that are not in the inventory are
listed on their own.

## Output

- `COVERAGE.md`: summary, inputs, tables by package, unit kind, subsystem, the SteeE slice (by reason
  and by class), the n-a entries, scenarios, units outside the inventory, every class, and the issues.
- `coverage.json` (`svm.coverage/1`): the same tables, plus every unit whose status is not unported
  with its evidence (spec lines, C# members, scenarios, the matching n-a entry number), one unit per line.
- `history.csv`: one row per date and commit (`git describe --always --dirty`); a later run on the same
  date and commit replaces its row.

Both report files depend only on the inputs: no clock and no machine paths.
