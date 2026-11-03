# Coding and documentation conventions

Use these rules when changing simulation code or documenting a port. For setup and
test commands, see [development](DEVELOPMENT.md) and [testing](TESTING.md).

## Write clearly

- Use plain English and short sentences. Use a comma or plain dash instead of an em dash.
- Keep code, docs and commit messages about the project; omit production notes and co-author lines.
- Explain non-obvious decisions in comments. Avoid repeating what the code already says.
- Link related guides instead of copying their instructions. Label historical results with their date.

## Write the specification first

A C# port needs a spec anchor in `spec/<subsystem>/`. Describe the behavior in our own
words: rules, constants, state changes, network roles and evidence. Evidence may be a
native address, a decompiler unit or a recorded scenario.

Write the port from the specification. Decompiled material stays in `local/` and must
not be committed. See the [provenance policy](../re/PROVENANCE.md).

## Keep original names

Ported classes, fields, functions, states and enum values keep their original names:
`SPlayerController`, `bFastSpeed`, `s_PlayerWalkingWeapon`, `EPhysics.PHYS_Walking`.
Namespaces follow the original package names, such as `Engine` and `SBase`. Native
ports keep native names such as `SingleLineCheck` and `physOnPipeH`.

[.editorconfig](../.editorconfig) disables naming rules that conflict with this policy.

## File layout

```text
SBase/SPlayerController.cs                         class, fields, global functions
SBase/SPlayerController.s_PlayerWalkingWeapon.cs   partial file for one state
Generated/                                        generated code
```

Use one class per file and one partial file per state (`<Class>.<State>.cs`). States are
nested classes. Latent operations such as `Sleep`, `FinishAnim` and `MoveTo` use
serializable `LatentFrame` data so they can be saved, replayed and compared.

Do not edit generated files by hand. See [code generation](CODEGEN.md) for regeneration,
state metadata and hand-written members. Content JSON can override generated defaults.

## Record provenance

The attributes in [OpenScct.Provenance](../src/OpenScct.Provenance/) link code to its
source evidence. Multiple attributes can describe one member. The
[coverage tool](../tools/OpenScct.Coverage/README.md) reads these links.

| Attribute | Use |
|---|---|
| `Uc` | Original script unit path and the SHA-1 of its bytecode |
| `UcNative` | Native function name and known game/editor addresses; use 0 for an unknown address |
| `PortOf` | Coverage unit ID and the spec file/heading that describes the port |
| `Provenance` | Evidence kind and source for a rule or constant |

Unit IDs use the prefixes `uc:`, `nat:`, `eng:`, `rep:`, `def:`, `fmt:` or `rl:`.
Use a real specification path and heading in `PortOf.Spec`.

## Constants

Put values taken from the original in a `K.*` class with a `Provenance` attribute.
Avoid unexplained literals in gameplay code. Label the evidence:

| Kind | Meaning |
|---|---|
| `Decoded` | Read from binaries, packages, defaults or disassembly |
| `Measured` | Observed in the running original through traces or captures |
| `Estimated` | A guess that still needs confirmation |

Use both attributes when a value has been decoded and measured.

## Deterministic simulation

Core, Physics, Game, Net and Content aim to produce identical results from identical
inputs. Cross-platform replay-hash comparison is planned; it is not yet a CI guarantee.

- Keep Godot out of simulation libraries. [Sim.props](../build/Sim.props) enforces this.
- Follow [BannedSymbols.txt](../BannedSymbols.txt). Use seeded simulation RNG streams
  instead of `System.Random`, and tick time instead of wall clocks or `Stopwatch`.
- Use `SvmMath` for banned transcendental functions. Do not use fused multiply-add.
- Keep original float calculations at 32-bit precision; avoid silent widening to double.
- Use ordered collections when iteration order affects gameplay. Do not decide gameplay
  by iterating a `Dictionary` or `HashSet`.
- Keep the fixed 60 Hz tick and the original physics sub-step rules.
- Store mutable state in the level and actors, not static fields.
- Aim for zero allocations per tick in steady state.

## Tests

C# tests use xUnit v3 and CsCheck through [Test.props](../build/Test.props), with the
Microsoft.Testing.Platform runner configured in [global.json](../global.json). Python
tests use pytest. Test data committed to Git must be synthetic; original recordings
and converted assets stay local.

See [testing](TESTING.md) for commands and [player-path testing](TESTING-PLAY.md) for
input, rendering and audio smoke tests.
