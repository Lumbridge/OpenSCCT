# reflect: reflection dumps of the running editor and game

`dump.py` reads the UnrealScript reflection data (classes, structs, states, functions, properties,
enums, constants and class defaults) out of a running `ChaosTheory_Editor.exe` or `SCCT Versus`,
and diffs two dumps. It only reads: the target is opened with `PROCESS_VM_READ |
PROCESS_QUERY_INFORMATION` and read with `ReadProcessMemory` (`procread.py`). Nothing is written,
injected or suspended.

Outputs are game-derived data. They go under `local/reflect/` (gitignored), never into a tracked path.

## Files

| File | What |
|---|---|
| `procread.py` | Read-only process access: reads, regions, loaded modules, PE header. `BytesMemory` is a synthetic address space for tests. |
| `ue2.py` | The engine layout, the object table, both name readers, the walker (`Engine.reflect`) and the class default decoder (`Engine.class_defaults`). |
| `dump.py` | Command line: `attach`, `probe`, `diff`, `sandbox`. |
| `test_reflect.py` | Tests on synthetic memory: walker, decoder, both name readers, diff. |

## Commands

Always `python -I -B`.

```
# dump a running process (editor or game is told apart by its image name)
python -I -B re/tools/reflect/dump.py attach --pid N --out local/reflect/game.json \
    --cdo-out local/reflect/game_cdo.json --cdo-packages SBase,Engine

# the whole capture in a sandbox: headless editor, its dump, the map, Play Level, the game dump
python -I -B re/tools/reflect/dump.py sandbox --sandbox F:\SCCT-Automation\svm\Sandbox \
    --map SteeE --out local/reflect --unslotted

# editor vs game
python -I -B re/tools/reflect/dump.py diff local/reflect/editor.json local/reflect/game.json \
    --out local/reflect/layout-diff.json --cdo-a local/reflect/editor_cdo.json --cdo-b local/reflect/game_cdo.json

# annotated raw words of named objects, for checking a layout by eye
python -I -B re/tools/reflect/dump.py probe --pid N --size 0x130 Engine.Actor Engine.Actor.Role

# tests
python -I -B -m unittest discover -s re/tools/reflect -p "test_*.py"
```

`sandbox` drives the RE+ private test tools (`headless.py`, `gamewait.py`, `game_tools.py`,
`mcp_server.py` in `--re-tools`, default the `SCCT_Versus_Reloaded_Editor_mb-main` checkout, or
`SVM_RE_TOOLS`). It follows the standing rules for the real game: editor Play Level only, on
a hidden desktop of its own (`--desktop SVMHeadless`), under the sandbox's play lock
(`gamewait.play_lock`: the slot's lock when the sandbox is slotted, the machine-wide one with
`--unslotted`), frame cap 60 for the run (restored after), and every process that runs from the
sandbox is ended at the end. The game is muted by `game_tools.Session` when it attaches (it
zeroes the audio volumes in memory, as every scripted run does); that is the only write any
part of the run makes to a process, and it is not the dumper's. A read-only watcher records
the sandbox's processes and windows (with a dialog's text) into `run.json`; a dialog from the
game side ends the run without being answered.

## Outputs

`editor.json`, `game.json` (`svm.reflect/1`): header (image, SHA-256, base, modules, name and
object table facts, self checks), then lists sorted by path, and `nameTable`.

- `classes`: path, package, address, `static` (the object lies in the exe image), super, within,
  configName, flags and their names, propertiesSize, defaultsSize, scriptSize, probe/ignore masks,
  `vtable` with `vtableSource`, `constructor` (ClassConstructor), classReps count and netFields
  (game only), dependencies count, packageImports, the names of its own properties, functions and states.
- `properties`: path, owner, kind, offset, elementSize, arrayDim, flags and names, category,
  repOffset / repIndex / repOwner (net properties), and by kind: bitMask, enum, struct, inner,
  propertyClass, metaClass, function (delegates).
- `functions`: path, flags and names, func (native pointer) with its RVA or `funcModule` when it
  points outside the exe, iNative, parmsSize, returnValueOffset, operPrecedence, numParms,
  scriptSize, repOffset (net functions), super, params in order (`!` = return value), locals.
- `states`, `structs`, `enums` (names in order), `consts` (values).

`*_cdo.json` (`svm.reflect.cdo/1`): per class of the chosen packages, `raw` (the whole
`UClass::Defaults` block as hex, header included) and `values` (every property the class has,
inherited ones too, decoded by kind: enums by name, objects by path, structs and arrays
recursively, arrays past 64 items cut short with their count).

`layout-diff.json` (`svm.reflect.diff/1`): per section the paths only in one side and the fields
that differ for the common paths, a per-package summary, name-table agreement, and the class
default values compared property by property (UObject header and pointers left out; localised
properties counted apart).

## Layout

Checked on the running editor and game (2026-10-23). Offsets are from the object's start.

| Type | Field | Both | Editor only | Game only |
|---|---|---|---|---|
| UObject | vtable, Index, Outer, ObjectFlags, Name, Class | +00 +04 +18 +1C +20 +24 | | |
| UField | SuperField, Next | +28 +2C | | |
| UStruct | Children, PropertiesSize, FriendlyName | +30 +34 +38 | | |
| UStruct | Script data, Script size | +3C +40 | | |
| UStruct | ScriptText, CppText, TextPos, Line | +44 +48 +4C +50 | | |
| UStruct | StructFlags, RefLink, PropertyLink, ConfigLink, ConstructorLink | +54 +58 +5C +60 +64 | | |
| UFunction | FunctionFlags, Func, iNative, RepOffset | +68 +6C +70 +72 | | |
| UFunction | ParmsSize, ReturnValueOffset, OperPrecedence, NumParms | +74 +76 +78 +79 | | |
| UState | ProbeMask, IgnoreMask, StateFlags, LabelTableOffset | +68 +70 +78 +7C | | |
| UState | size | | 0x8C | 0x88 |
| UClass | ClassFlags, ClassWithin, ClassConfigName | | +8C +90 +94 | +88 +8C +90 |
| UClass | ClassReps, NetFields (filled in the game only) | | +98 +A4 | +94 +A0 |
| UClass | Defaults, Dependencies, PackageImports | | +B0 +D8 +E4 | +AC +D4 +E0 |
| UClass | ClassConstructor | | +108 | +104 |
| UProperty | ArrayDim, ElementSize, PropertyFlags, Category, Offset | +30 +32 +34 +38 +3C | | |
| UProperty | PropertyLinkNext, ConfigLinkNext, ConstructorLinkNext, NextRef, RepOwner | +40 +44 +48 +4C +50 | | |
| UProperty | RepOffset, RepIndex | | +60 +62 | +54 +56 |
| UProperty | Enum / BitMask / Struct / Inner / PropertyClass / Function; MetaClass | | +64; +68 | +58; +5C |
| UEnum | Names (TArray of FName) | +30 | | |
| UConst | Value (FString) | +30 | | |

The tables: object table `GObjObjects` at 0x11697B70 (editor) and 0x10CC9E80 (game). Names: the
editor's GNames array at 0x1169CFBC (FNameEntry: +0 Index, +0C 8-bit text); the game's names are
found by scanning its heap for FNameEntry blocks (+0 and +4 hash links, +8 std::wstring with a
16-byte buffer or a pointer, +18 size, +1C capacity, +20 Index). Strings (FString) are 8-bit in
the editor and UTF-16 in the game.

Flag names (CPF_, FUNC_, CLASS_, STATE_) are the usual Unreal Engine 2 ones; the values are read
from memory, the names are assumed.

Vtables: from live instances when the class has any; else from another class with the same
ClassConstructor that has instances; else the last store of an image vtable into the new object
in the constructor's code (needs `capstone`). Each class says which (`vtableSource`).

## Limits

- The game loads classes on demand, so a game dump holds the classes the loaded map and the
  front end use (SteeE: 613 of the editor's 871, 218 of 248 in SBase). The editor loads every
  package whole.
- Name indices below about 1.29 million are fixed and agree between the two processes; the rest
  are given out at run time and differ. Compare by text.
- The game's heap scan can meet stale entries; a clash is counted and the first linked entry wins.
- `values` decode what the property says it is. Pointers inside a raw block are only valid in
  the process that was dumped.
