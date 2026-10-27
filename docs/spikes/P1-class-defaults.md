# P1: exact class default objects from the script packages

**Verdict: GO.** A field-by-field reader of the v300 class export, with the bytecode walked token by
token, finds the default properties exactly. All 665 classes of Core, Engine, SBase, GUI and IpDrv
decode with no error, no unknown property and no tag whose size disagrees with its value. Against
the live class default objects of the running game, 17,986 values agree and the 8 that differ are
all values the engine replaces at run time from `.ini` files or references into packages that the
process had not loaded.

Date: 2026-10-23. Decoder: `re/formats/scctformats/classdefaults.py`
(`python -B -m scctformats.classdefaults --live local/reflect/game_cdo.json`). Outputs:

| File | Content |
|---|---|
| `local/formats/classdefaults.json` | per class: super, within, config, flags, probe and ignore masks, script sizes, imports, dependencies, hide categories, shadowed names, and `props`: exactly what the class stores |
| `local/formats/classdefaults_effective.json` | `props` merged down the super chain (static arrays as full lists) |
| `local/formats/classdefaults_localized.json` | the same with the English `.int` text applied, which is what the game holds |
| `local/formats/classdefaults_vs_<dump>.json` | comparison with a live dump, every difference listed with its reason |

## Why the earlier dumps were wrong

The earlier tool searched backwards for the earliest offset from which a tagged property list
parsed cleanly to the end of the class export. The bytes before the defaults (the hide category
names, the config name, the dependency table) often parse as a short tagged list too, so the search
started too early and invented properties from header bytes:

| Class | search start | real start | what the search produced first |
|---|---:|---:|---|
| STriggerPlayerStart | 122 | 171 | `ChangeEnumAction`, `Force`, `EchelonHeat` from the 14 hide category names |
| SPresenceTrigger | 153 | 157 | `System`, `Y = 29` from the config name |
| SWeaponDefense | 2196 | 2332 | 136 bytes of header read as properties, then misaligned values |

Other causes fixed on the way:

- Imported object paths: scctpkg's `full_name` puts the current package in front of an import's path
  (`Engine.Core.Object.Vector`), so imports never resolved; the reader builds paths itself.
- Array element types were guessed; they now come from each `ArrayProperty`'s `Inner` property.
- A `ClassProperty` is tagged with the object type id (5) and a `DelegateProperty` with the old fixed
  string id (7); both are now decoded by the declared property, not the tag type.
- Shadowed names: a class can declare a property again with the name of an inherited one (20
  classes, e.g. `SProjFrag.Damage` int over `Projectile.Damage` float, `SoundTrigger.SoundVolume`).
  A stored value belongs to the most derived declaration (UE2 searches its own fields first).
  Dumps keyed by name show only one of the two, so the comparison skips those values (4 in the dumps).

## Format (package version 300)

A UClass object has no tagged property list of its own; every other object (functions, states,
properties, enums, consts, structs) starts with one, which is just `None` for these.

| Part | Fields |
|---|---|
| UField | objref SuperField, objref Next |
| UStruct | objref ScriptText, objref Children, name FriendlyName, objref CppText (v >= 120), i32 Line, i32 TextPos, i32 ScriptSize (in-memory bytes), bytecode |
| UFunction | UStruct, u16 iNative, u8 OperPrecedence, u32 FunctionFlags, u16 RepOffset if FUNC_Net (0x40) |
| UState | UStruct, u64 ProbeMask, u64 IgnoreMask, u16 LabelTableOffset, u32 StateFlags |
| UClass | UState, u32 ClassFlags, 16-byte Guid, compact n x (objref Class, i32 Deep, u32 ScriptTextCRC), compact n x name PackageImports, objref Within, name ConfigName, compact n x name HideCategories, then the defaults |
| UProperty | UField, i32 ArrayDim, u32 PropertyFlags, name Category, u16 RepOffset if CPF_Net (0x20) |
| subclasses | Byte: objref Enum; Object: objref Class; Class: + objref MetaClass; Struct: objref Struct; Array: objref Inner; FixedArray: objref Inner, i32 Count; Map: 2 objrefs; Delegate: objref Function; Bool, Int, Float, Name, Str: nothing |
| UEnum / UConst | UField + compact n x name / UField + FString |

Bytecode: the disk form differs from ScriptSize because object refs and names are compact on disk but
4 bytes in memory, so the reader replays the engine's expression serializer and counts memory bytes.
This build renumbers the native tokens: 0x46 to 0x55 are extended natives (one more byte), 0x56 and
up are natives; stock UE2 uses 0x60 and 0x70. Operand table of tokens 0x00 to 0x45 (E expression,
P parameters up to 0x16, O object, N name, B byte, W word, I int, F float, S zero-terminated string):

| Operands | Tokens |
|---|---|
| none | 08 0B 16 17 25 26 27 28 2A 2D 30 31 |
| O | 00 01 02 20 29 |
| E | 04 0D 0E 37 |
| EE | 0F 10 14 1A 45 |
| EEE / EEEE | 40 41 / 11 |
| OP / NP | 1C / 1B 38 |
| ON / N | 43 / 21 44 |
| E W B E | 12 19 |
| B E | 05 39 |
| W E / E W / W | 07 09 18 / 2F / 06 |
| OE / OEE | 13 2E 36 / 32 33 |
| I, III, F, FFF, B, S | 1D, 22, 1E, 23, 24 2C, 1F |
| case: W, then E unless W = 0xFFFF | 0A |
| label table: (N, u32) until the name None | 0C |
| invalid | 03 15 2B 34 35 3A to 3F 42 |

Tagged values: the tag is the usual UE2 one (name, info byte, struct name for type 10, size by code,
array index when bit 7 is set on a non-bool). Vector, Rotator and Color structs are stored natively;
every other struct is a nested tagged list (the struct item serializer skips the tagged form only for
those three names). Tag sizes leave out the cooked global name index of every name in the value.

Evidence: ChaosTheory_Editor.exe `UField` 0x10fb4f00, `UStruct` 0x10fb60d0, `SerializeExpr`
0x10fb64e0 (jump table at 0x10fb6a50 / 0x10fb6ac4), `UState` 0x10fb6350, `UFunction` 0x10fb6b10,
`UClass` 0x10fb8c60, `UProperty` 0x10fe1790 and its subclasses, `FPropertyTag` 0x10fb5cc0,
`UStructProperty::SerializeItem` 0x10fe4400 (skips the tagged form for three hard-coded names).
`UBoolProperty::Serialize` writes an extra i32 only for archives that are neither loading nor saving,
so packages have none.

## Results

| Measure | Value |
|---|---|
| classes (Core 7, Engine 142, SBase 248, GUI 265, IpDrv 3) | 665, 0 errors |
| stored default values (tags) | 5,447 tags, 4,575 top-level properties; 0 unknown, 0 type, 0 size mismatches |
| other field objects read to the last byte | 6,440 functions, 435 states, 94 structs, 129 enums, 963 consts, 17,132 properties |
| bytecode walked | 265,355 tokens, 794,894 bytes in memory, 634,039 on disk |

Live comparison (`local/reflect/game_cdo.json`, the game process on SteeE, and `editor_cdo.json`,
the editor; both from the reflection dump of 2026-10-23; only Engine and SBase classes are dumped):

| Against | classes | agree | differ | reasons for every difference |
|---|---:|---:|---:|---|
| game, raw defaults | 353 | 17,896 | 95 | 70 localized text, 23 config, 2 unloaded package refs |
| game, with `.int` text | 353 | 17,986 | 8 | 6 config (`GameEngine.UseSound` from the ini, 5 `SPlayerProfile` values from the profile), 2 sounds in `Armes_impacts`, not loaded (`SInfirmary.HealSound`, `SPresDetector.Snd_ScanPresDec`) |
| editor, raw defaults | 390 | 19,417 | 8 | the same 8 (the editor does not apply `.int` text) |

Completeness: the only non-zero live values that no class stores are 5 `SPlayerProfile` profile
settings. 53 live classes have no script export at all (intrinsic classes such as `StaticMesh`,
`Level`, `Model`); their defaults exist only in the live dumps.

Values asked for:

| Value | Decoded | Live game |
|---|---|---|
| `SWeaponDefense.BaseDamage` | 17 (`SWeaponDefense_Uzi` 10, `SLethalWeapon` 20) | 17, 10 |
| `RemoteRole` of weapons and gadgets | `ROLE_SimulatedProxy` (2), stored on `Engine.Inventory`, inherited by `SWeapon`, `SWeaponDefense*`, `SGadget` | same |
| `SComputerObjectiveTrigger.DominationCaptureTime` | 12 | 12 |
| `CaptureTime` | 5 (`SObjectiveTrigger`, inherited by `SComputerObjectiveTrigger`), 0 on `SFlag` | same |
| `SFlag.ResetTime` | 60 (`SDisk` 20) | 60 |
| `UsableBy` | `SInterrupteur` 0 `U_DEFAULT_USABILITY` (not stored), `SInteractObject` 1 `U_BOTH`, spy-side interactions 3 `U_SPAWNOFFENSE`, merc-side 2 `U_SPAWNDEFENSE` | same |
| `STriggerPlayerStart` | stores only `bStatic = False` | same |
| `SPresenceTrigger` first value | `DetectedClass = SBase.SPAWNDEFENSE` | same |
| `LevelInfo.GEActivationMask` | 0 (no class stores it) | 0 |

## Verified facts and assumptions

- Verified: the layout (every class, function, state, struct, enum, const and property export of the
  five packages read to its end), the token table (all bytecode consumed exactly to ScriptSize), and
  the decoded values against two live dumps.
- Not covered: GUI and IpDrv defaults are decoded but absent from the live dumps; Core's 7 classes
  too. Config values (`.ini`, profiles) are not applied; only the English `.int` text is.
- GUI class defaults point at many subobjects (GUILabel, GUIButton exports); those objects' own
  properties are ordinary tagged lists that scctpkg can read, but they are not class defaults and
  are not in this output.

## Next steps

- Use `classdefaults_localized.json` as the class-defaults pack in svmbuild P1 and for the instance
  diffs in P4 (an instance property absent from a map is the class default here).
- Hand the reader to R2: `ScriptWalker` already walks every function body; the decompiler can reuse
  the token table and the field readers.
- Apply `Default.ini` / `DefaultProfile.ini` config sections when the server needs config defaults.
