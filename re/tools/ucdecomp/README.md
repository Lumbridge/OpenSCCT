# UnrealScript decompiler

`ucdecomp` reads SCCT Versus v300 script packages (Core, Engine, SBase, GUI and IpDrv) and
writes UnrealScript text and a JSON index. It uses the vendored `scctpkg`, with the Bridge
checkout as a fallback. Output belongs in ignored `local/re-out/uc/`; never commit it.

See the [RE setup guide](../../README.md) for prerequisites and the
[decompiler study](../../../docs/spikes/R2-ucdecomp.md) for the recorded validation results.

## Run

Run from `re/tools`. Replace `<install>` with the game folder:

```powershell
python -B -m ucdecomp check --game "<install>"
python -B -m ucdecomp decompile --game "<install>"
python -B -m ucdecomp dis --game "<install>" SBase SPlayerController.PlayerTick
python -B -m pytest ucdecomp/tests -p no:cacheprovider
```

`check` fails unless every script re-encodes byte for byte. Use `--packages Core,Engine` to
limit the packages, `--json <file>` for a check report, or `--out <folder>` for decompiled output.

## Modules

| Module | Role |
|---|---|
| `pkgload.py` | finds and imports `scctpkg`, locates `System/_PC_` |
| `loader.py` | v300 UField hierarchy: UStruct, UFunction, UState, UClass, properties, enums, consts |
| `tokens.py` | token table (operand shapes, in-memory sizes, cast codes) |
| `bytecode.py` | bytes to token tree with file and in-memory offsets, and back |
| `universe.py` | cross-package references, native table, probe names |
| `structure.py` | control-flow structuring and lowering back to jumps |
| `printer.py` | expressions, statements, operators with precedence, enum and const naming |
| `defaults.py` | defaultproperties and sub-objects (tagged lists via `scctpkg.properties`) |
| `emit.py` | class files and `uc-index.json` |
| `check.py` | T1 re-encode, T2 lowering, argument counts and skip sizes |
| `disasm.py` | flat token listings |

Unit tests use only byte sequences and a small package written by the tests themselves
(`tests/synth.py`). Results on the real packages are in `docs/spikes/R2-ucdecomp.md`.
