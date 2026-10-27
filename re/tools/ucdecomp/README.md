# ucdecomp

UnrealScript bytecode decompiler for the SCCT Versus v300 script packages (Core, Engine, SBase, GUI,
IpDrv). It reads the packages through `scctpkg` (the copy in `tools/scctpkg`, else the Bridge checkout)
and writes UnrealScript text plus a JSON index. Decompiled output is game-derived: it goes to
`local/re-out/uc/` (gitignored) and never into tracked paths.

Run from `re/tools`:

```
python -B -m ucdecomp check --game "<install>" [--packages Core,Engine] [--json out.json]
python -B -m ucdecomp decompile --game "<install>" [--out <dir>] [--packages ...]
python -B -m ucdecomp dis --game "<install>" SBase SPlayerController.PlayerTick
python -B -m pytest ucdecomp/tests -p no:cacheprovider
```

`check` exits non-zero unless every script re-encodes byte for byte.

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
