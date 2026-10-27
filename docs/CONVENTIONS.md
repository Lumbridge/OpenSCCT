# Conventions

## Writing

- Plain English, short sentences. No em dashes: use a plain dash or a comma.
- Code, docs and commit messages talk about the project only: no notes on how the text was produced
  and no co-author lines.
- Code comments only where they help a future reader: why, not what.

## Spec first

No C# port without a spec anchor. A spec in `spec/<subsystem>/` describes one unit in our own words:
rules, constants with their evidence tag, state diagrams, network role and evidence (Ghidra address,
decompiler unit, scenario). The C# is written from the spec, not from decompiled text. Decompiled
material never leaves `local/` (see `re/PROVENANCE.md`).

## Names: 1:1 with UnrealScript

- A ported class has its original name: `SPlayerController`, `SBasePawn`, `SPAWNDEFENSE`.
- The namespace is named after the UnrealScript package (Core, Engine, SBase, GUI, IpDrv).
- Fields, functions, states, enums and enum values keep their original names and values:
  `bFastSpeed`, `PlayerMove`, `s_PlayerWalkingWeapon`, `EPhysics.PHYS_Walking = 1`.
- Ports of native engine code keep the native function names: `SingleLineCheck`, `physOnPipeH`.
- `.editorconfig` turns off the naming rules that would fight this (IDE1006, CA1707, CA1711).

## File layout per class

```
src/OpenScct.Game/SBase/SPlayerController.cs                          class body, fields, global functions
src/OpenScct.Game/SBase/SPlayerController.s_PlayerWalkingWeapon.cs    one partial per state
src/OpenScct.Game/Generated/{Defaults,Replication,Dispatch}/          generated, never edited by hand
```

- One class per file, plus one partial file per state, named `<Class>.<State>.cs`.
- States are nested classes. Dispatch (state overrides, `ignores`, `Global.`, `Super.`) is generated.
- Latent code (`Sleep`, `FinishAnim`, `MoveTo`...) is stored as serializable `LatentFrame` data, so a
  running state can be saved, replayed and compared.
- Generated defaults can be overridden by content JSON, which is how data mods work.

## Provenance attributes

Every port says where it came from. The attributes live in `OpenScct.Provenance`; the coverage tool
(`svm coverage`) reads them. Each one may be stacked.

| Attribute | Goes on | Says |
|---|---|---|
| `[Uc("SPlayerController.s_PlayerWalkingWeapon.PlayerMove", Sha1 = "...")]` | every port of script code | the unit path, Class[.State].Function, and the SHA-1 of the original bytecode the port was written from, so ports whose source changed can be flagged |
| `[UcNative("APawn::physOnPipeH", Exe = 0x..., Editor = 0x...)]` | every port of native engine code | the native name as in the engine guard strings, and its address in the game and editor executables (0 when unknown) |
| `[PortOf("nat:APawn::physWalking", Spec = "spec/physics/walking.md#rules")]` | anything that implements a coverage unit | the coverage unit ID (`uc:`, `nat:`, `eng:`, `rep:`, `def:`, `fmt:`, `rl:`) and the spec anchor it was written from |
| `[Provenance(ProvenanceKind.Decoded, "SBase.u SPlayerController defaults")]` | constants in `K.*`, rules | the evidence kind and where the value came from |

## Constants

- Every number taken from the original lives in a `K.*` class with a `[Provenance]` attribute, never
  inline. Examples: `fErrorMax` 1.73, `NetServerMaxTickRate` 20, `MaxClientRate` 20000.
- Each constant carries an evidence kind (`ProvenanceKind`):
  - **Decoded**: read from the original binaries or packages (script, class defaults, disassembly)
  - **Measured**: observed in the running original (traces, live reads, captures)
  - **Estimated**: a best guess that still needs confirming
- A value that was both decoded and measured gets both attributes.

## Determinism

The simulation libraries (`OpenScct.Core`, `OpenScct.Physics`, `OpenScct.Game`, `OpenScct.Net`, `OpenScct.Content`) must give
bit-identical results on every machine. Replay hashes from win-x64 and linux-x64 will be compared in CI.

- No Godot. `build/Sim.props` fails the build if a sim library references it.
- Banned APIs (`BannedSymbols.txt`, RS0030 is an error in sim code):
  - `System.Random`: use the seeded sim RNG streams.
  - `DateTime.Now`, `DateTime.UtcNow`, `Environment.TickCount`, `Stopwatch`: sim time comes from the
    tick counter.
  - `Math` and `MathF` transcendentals (`Sin`, `Cos`, `Tan`, `Asin`, `Acos`, `Atan`, `Atan2`, `Exp`,
    `Log`, `Pow`): use `SvmMath`, which uses the UE2 sine table.
  - `FusedMultiplyAdd`: no FMA in sim code.
- float32 wherever the original uses float. No silent widening to double in sim maths.
- Use ordered collections wherever iteration order can change a result. Never iterate a `Dictionary`
  or `HashSet` to decide gameplay.
- Fixed 60 Hz tick, with the UE2 sub-step rule inside physics.
- No mutable statics in sim code. State lives in the level and its actors.
- Zero allocations per tick in steady state.

## Tests

- C# tests use xunit v3 and CsCheck for property tests, through `build/Test.props`, and run on the
  Microsoft.Testing.Platform runner (`global.json`): `dotnet test --solution OpenSCCT.slnx`.
- Python tests use pytest.
- Test data in git is synthetic (`fixtures/`, the generated TestBox map).
