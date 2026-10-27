# Class skeleton generator (OpenScct.CodeGen)

`tools/OpenScct.CodeGen` writes a C# skeleton for every UnrealScript class of the Core, Engine and SBase packages:
the fields with their original names and mapped types, enums and structs, the decoded class defaults, and
metadata (the unit manifest, states and replication as data). It writes no function bodies. Ports are written by
hand from the specs, in partial files next to the generated ones.

## Running it

```powershell
dotnet run --project tools/OpenScct.CodeGen            # regenerate (needs the local inputs below)
dotnet run --project tools/OpenScct.CodeGen -- --check # exit 1 when a generated file is missing, different or stale
```

The run takes a few seconds, rewrites only files whose content changed and deletes stale `*.g.cs` files in its
output folders. Its report goes to `local/codegen/report.md`: counts, every decision about hand-written members,
renamed and hiding members, defaults it could not apply and functions with the SCCT "to all clients" flag. The
exit code is 1 when the report has errors.

### Inputs (local only)

| Input | Default path | Made by |
|---|---|---|
| Decompiler index | `local/re-out/uc/uc-index.json` | `python -B -m ucdecomp decompile` (`re/tools/ucdecomp`) |
| Reflection dumps, game first | `local/reflect/game.json`, `local/reflect/editor.json` | `re/tools/reflect/dump.py` |
| Class defaults, localized | `local/formats/classdefaults_localized.json` | `python -B -m scctformats.classdefaults` (`re/formats`) |
| Class defaults, raw (class flags) | `local/formats/classdefaults.json` | same |

The decompiler index gives classes, properties, functions (bytecode SHA-1, flags, signatures), states and
replication blocks. The reflection dumps give the exact property types (enum, struct, class and inner types),
native offsets and sizes, enum members, struct members, consts and raw flags; the editor dump fills what the game
dump lacks. Override any path with `--uc-index`, `--reflect` (repeatable), `--defaults`, `--defaults-raw` and
`--report`; `--repo` sets the repository root.

The generated files are tracked. They hold names, types, numbers, decoded default values and hashes, never bodies
and never the game's localized text (see Class defaults). CI cannot regenerate them (it has no game data), so regenerate after the
decompiler, reflection or defaults tools change, and commit the result with the generator change that caused it.

## Output

```
src/OpenScct.Core/Generated/Core/     namespace OpenScct.Core         UObject.g.cs, U<Class>.g.cs, Core.Enums.g.cs,
                                                            Core.Structs.g.cs, CorePackage.g.cs, FixedArrays.g.cs
src/OpenScct.Core/Generated/Engine/   namespace OpenScct.Core.Engine  <Class>.g.cs, Engine.Enums.g.cs, Engine.Structs.g.cs,
                                                            EnginePackage.g.cs
src/OpenScct.Game/Generated/SBase/    namespace OpenScct.Game.SBase   <Class>.g.cs, SBase.Enums.g.cs, SBase.Structs.g.cs,
                                                            SBasePackage.g.cs
```

One file per class. Everything is sorted (classes and enums by name, fields in declaration order taken from the
native layout, functions in source order, states by name), written with LF line endings and no timestamps, so a
run with the same inputs gives the same bytes whatever the input order.

### Names

| UnrealScript | C# |
|---|---|
| `Engine.Actor` and every subclass | the original name: `Actor`, `Pawn`, `SInterrupteur`, `SPAWNDEFENSE` |
| `Core.Object` | `OpenScct.Core.UObject` |
| any other UObject class (materials, interactions, commandlets...) | the engine's C++ name, `U` + name: `UInteraction`, `UMaterial`, `UCommandlet` |
| structs | `F` + name, top level in the owner's package namespace: `FColor`, `FPointRegion`, `FWidgetInfo` |
| `Vector`, `Rotator`, `Coords` | the runtime's `FVector`, `FRotator`, `FCoords` |
| enums | the original name, top level in the package namespace: `EDrawType`, `Usable` |
| fields, enum members, consts | the original names; a C# keyword gets `@` (`@default`) |

The U and F prefixes keep generated names apart from Godot, System and runtime types (`UMaterial` and Godot's
`Material`, `UConsole` and `System.Console`, `FColor` and Godot's `Color`). A field named like its class gets a `_`
suffix. Each class's metadata keeps the original name of every renamed member.

Intrinsic engine classes (C++ only, no script) that a field refers to get a stub with their fields (`ULevel`,
`UViewport`, `UClient`...). A class whose super class is outside the generated packages derives from its nearest
generated ancestor (`SLANClientLink` extends `IpDrv.UdpLink` and derives from `InternetInfo`).

### Types

| Property | C# |
|---|---|
| `bool`, `byte`, `int`, `float`, `string` | `bool`, `byte`, `int`, `float`, `string` (initialised to `""`) |
| `name` | `Name` |
| byte with an enum | the enum (`: byte`) |
| struct | the mapped struct (`partial record struct`, value equality) |
| actor reference | the class, nullable: `Pawn?`, `SBasePawn?` |
| other object reference | the `U` class, nullable: `UInteraction?` |
| content object (Material and subclasses, Primitive (meshes, static meshes, models) except MeshInstance, MeshAnimation, Animation, Sound, Font, Palette, ParticleEmitter, AnimNotify) | `AssetRef`: the object path, never the object |
| `class<T>` | `ClassRef`: the class path |
| `delegate<F>` | `DelegateRef` |
| `array<T>` | `List<T>` (initialised empty) |
| static array `T X[N]` in a class | `readonly T[] X = new T[N]` |
| static array in a struct | `FixedArrayN<T>`, an inline array, so struct copies stay copies |

`AssetRef`, `ClassRef`, `DelegateRef` and the metadata types live in `OpenScct.Core.Uc`.

Semantic gaps to keep in mind when porting: UnrealScript copies dynamic arrays and static arrays on assignment and
copies arrays inside structs with the struct; in C# a `List` or class array assignment shares it. Strings in a
`default(FStruct)` are null; treat null like `""`.

## Hand-written parts

The generator reads the hand-written C# of `src/OpenScct.Core` and `src/OpenScct.Game` as syntax (Roslyn, no compile, so it
works while generated code is stale), skipping `Generated` folders. A hand-written class with the same namespace
and name as a generated one is its other partial part and must be declared `partial`.

- A script property that the hand-written part of its class (or of an ancestor, when no script class in between
  declares the name) already declares as a field or property is not generated. The metadata marks it
  `HandWritten`, and SetDefaults still assigns its default when the member is a field or a property with a setter
  it can reach and has the same type (`Actor.Location`, `Actor.Role`, `LevelInfo.DefaultGravity`).
- A script property that shadows an inherited one (SCCT has a few, for example `SProjFrag.Damage` as int over
  `Projectile.Damage` as float) is generated with `new`.
- A hand-written member in a subclass that would hide a generated ancestor member stops that member from being
  generated and is an error in the report: move the hand-written member to the class that declares it.
- `Core.Object`'s own properties (`Class`, `Name`, `ObjectFlags`, `Outer`, `ObjectInternal`) are object internals
  the runtime keeps natively; they appear only in the metadata.
- Hand-written enums with the original name (`EPhysics`, `ENetRole`, `ENetMode`) are used instead of generating
  them, after checking their members and values.

Never edit a `.g.cs` file. Add a partial file instead, or change the generator. After adding a hand-written member
named like a script property, or a constructor to a generated class, run the generator again so the generated part
drops the property and the factory matches.

## Class defaults

`UObject`'s constructor calls the virtual `SetDefaults()` once. Each generated class overrides it, calls
`base.SetDefaults()` first, then assigns only the values that differ from its parent's effective defaults (a missing
value counts as zero, None or empty). So a new object of any class, ported or not, starts with the decoded values of
the original class default object:

```csharp
protected override void SetDefaults()
{
    base.SetDefaults();
    StaticMesh = new AssetRef("SGameplayObjects.SGPO_SM.trigger_standard");
    DrawType = EDrawType.DT_StaticMesh;
    UseRelativeRotation = new FRotator(0, 32768, 0);
    UseRelativeLocation = new FVector(55f, 0f, 0f);
    InteractionSound = new AssetRef("Sound_Persos.divers.RA_interrupteur");
}
```

- Floats are written as the shortest literal that round-trips the exact binary32 value; enums by member name;
  static arrays per element; dynamic arrays with `UcDefaults.Set`; structs whole, then any inline-array elements.
- Localized string properties (CPF_Localized: strings, string static arrays and string dynamic arrays) are not
  written as literals: the text belongs to the game. SetDefaults reads them at run time with `UcText.Get` /
  `UcText.Fill` (`OpenScct.Core.Uc`) from the player's built `shared/classes.json`, in the language they chose, and
  keeps the inherited value when that file or the key is missing. `ContentPaths.Configure` points `UcText` at the
  launcher's content; a checkout falls back to `.content/shared/classes.json`. Without content these strings are
  empty, so tests that need the text must have built content.
- Order of construction: field initializers of every class run first, then `SetDefaults()` (from the `UObject`
  constructor), then the constructor bodies from base to derived. A hand-written constructor body therefore wins
  over a decoded default; the report lists the literals that disagree. A hand-written field initializer never
  wins: the declaring class always writes the decoded value.
- Config and per-instance values are not part of class defaults: maps apply their props after construction, and
  config files are a later step.

To add construction logic to a generated class, write a constructor in its partial file. Do not override
`SetDefaults`.

## Metadata

Every generated class has a nested `UcMeta` with `UcMeta.Class` (a `UcClassInfo`), returned by the virtual
`GetUcClass()`, and one `UcStateInfo` shortcut per state (`UcMeta.s_LockedByChaff`).

- `UcClassInfo`: path, super class info, C# type, flags (`UcClassFlags`), config name, native object size, factory
  (null for abstract classes), and:
  - `Properties` (`UcPropertyInfo`): original name, UnrealScript type text, kind, flags (`UcPropertyFlags`, the
    UE2 `CPF_*` bits), static array size, editor category, native offset and element size in the game build,
    replication offset, C# member name, hand-written flag.
  - `Functions` and each state's `Functions` (`UcFunctionInfo`), the unit manifest: name, bytecode SHA-1, flags
    (`UcFunctionFlags`: simulated, net, reliable, native, latent, exec, event, static, final, delegate and the SCCT
    `FUNC_NetAllClients` 0x01000000), native index, script size, return type, parameters (name, type, flags),
    replication offset and latent calls. `UnitId` is the coverage id (`uc:SBase.SInterrupteur:Trigger`,
    `uc:Pkg.Class:State.Func`, `nat:Pkg.Class.Func` for natives); `UcPath` is the `[Uc]` path
    (`SBase.SInterrupteur.Trigger`).
  - `States` (`UcStateInfo`): name, `Extends`, flags (auto, simulated, editable), ignores, labels with bytecode
    offsets, code SHA-1 and size, latent calls; `CodeUnitId` is `uc:Pkg.Class:State@code`.
  - `Replication` (`UcReplication`, below). `DefaultsUnitId` is `def:Pkg.Class`; `UcPropertyInfo.RepUnitId` is
    `rep:Pkg.Class.Var`.
- `CorePackage.Info`, `EnginePackage.Info` and `SBasePackage.Info` list the classes of each package. A
  `UcClassIndex` built from them finds classes by path, name, `ClassRef` or C# type, and functions and state code by
  unit id or `[Uc]` path. There is no global registry; build an index where you need one.
- `UcManifestCheck.Check(index, assemblies...)` checks every `[Uc]` attribute: the path must name a function, state
  code, state or class, and a given Sha1 must equal the manifest's. `OpenScct.CodeGen.Tests` runs it over OpenScct.Core and
  OpenScct.Game, so a wrong or outdated Sha1 fails the tests.

### Replication

`UcReplication.Blocks` holds each `reliable if` / `unreliable if` block of the class in source order: the
condition, the members (variables, and functions with their flags) and the block's offset. Conditions are
`RepExpr` trees, not code:

```csharp
// SInterrupteur, first block: bNetInitial && Role == ROLE_Authority
RepExpr.And(RepExpr.Net(RepNetVar.bNetInitial), RepExpr.Eq(RepExpr.Net(RepNetVar.Role), RepExpr.Enum("ENetRole", "ROLE_Authority", 4)))
```

Node kinds: `Bool`, `Int`, `Enum` (type, member and value), `None`, `Property` (a property of the actor, by name),
`Member` (a property of the object another node refers to, as in `Base.bWorldGeometry`), `NetVar` (`Role`,
`RemoteRole`, `bNetInitial`, `bNetOwner`, `bNetDirty`, `NetMode` for `Level.NetMode`), `Not`, `And`, `Or` and the
six comparisons. `And` and `Or` short-circuit. The interpreter belongs in OpenScct.Net: it supplies the `NetVar` values
per connection (the roles as the replicating side sees them) and reads properties through
`UcClassInfo.FindProperty(name).CSharpName`. `InheritedFunctions` lists functions a class redefines that keep the
replication of an ancestor's block.

## Porting a class

A port is a partial file in the class's folder, for example `src/OpenScct.Game/SBase/SInterrupteur.cs`, written from the
spec. Every ported function carries `[Uc]` with the SHA-1 from the manifest and `[PortOf]` with the unit id and the
spec anchor. Read both from the generated file (`UcMeta.Class.Functions`) or from the coverage inventory.

```csharp
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// Switches and hackable panels, written from spec/objectives/switch-hack.md.
public partial class SInterrupteur
{
    [Uc("SBase.SInterrupteur.PreBeginPlay", Sha1 = "e2ec1fd17e52a406fadf192ace44a71434bf5c6f")]
    [PortOf("uc:SBase.SInterrupteur:PreBeginPlay", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void PreBeginPlay()
    {
        base.PreBeginPlay();
        bInitialSecondaryObjective = bSecondaryObjective;
        bSecondaryObjective = false;
    }

    [Uc("SBase.SInterrupteur.Trigger", Sha1 = "a768534700e8919e20ec7b8af78e6f31da50bd68")]
    [PortOf("uc:SBase.SInterrupteur:Trigger", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        if (InteractionObj is null)
        {
            return;
        }

        InteractionObj.bUsable = !InteractionObj.bUsable;
        if (bInitialSecondaryObjective)
        {
            bSecondaryObjective = InteractionObj.bUsable;
        }
    }
}
```

The fields (`bSecondaryObjective`, `InteractionObj`, `bUsable`), their defaults and the metadata come from the
generated parts; the file adds behaviour only. A function that an ancestor declares first (`AcceptStickableGPO` is
declared by `SBasicGamePlayObject`, `ForceTriggerableEvenIfMoving` by `Actor`) is ported in that ancestor as
`virtual` and overridden here.

### States

States are hand-written `ActorState` classes built from the generated `UcStateInfo`. `UcActorState` takes the class
and state names, the auto flag and the labels from it and checks the super state against `Extends`:

```csharp
public partial class SBasicGamePlayObject
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_LockedByChaff.Instance);

    public override StateTable GetStateTable() => ClassStates;

    [Uc("SBase.SBasicGamePlayObject.s_LockedByChaff")]
    public class s_LockedByChaff : UcActorState
    {
        public static readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, null);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SBasicGamePlayObject.s_LockedByChaff.Timer", Sha1 = "85aed8544109c027507849686ebb9e4943e8b406")]
        [PortOf("uc:SBase.SBasicGamePlayObject:s_LockedByChaff.Timer", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
        public override void Timer(Actor self) => self.GotoState(((SBasicGamePlayObject)self).ChaffLockPreviousState);
    }
}
```

- Pass the super state's `Instance` when `Extends` is set: a state of the same class for `extends`, or the parent
  class's state when a subclass redefines it (then `Extends` is the state's own name). Derive the C# class from that
  super state's class.
- Port `Ignores` as empty overrides.
- Each label's suggested entry Pc is its bytecode offset plus one (`UcStateInfo.LabelPcs`), which keeps labels in
  order with room for resume points between them. The `Code` switch uses those values.
- The state code's own `[Uc]` path is `Pkg.Class.State@code` with the state's SHA-1, and its unit is
  `uc:Pkg.Class:State@code`.

## What the generator does not do

- No function bodies, no state dispatch, no generated `StateTable` (the planned Roslyn generators in
  `OpenScct.Generators` can take that on, using this metadata).
- No replication layout or interpreter: OpenScct.Net builds those from `UcReplication`.
- Map props are applied by OpenScct.Content's loader, which does not convert `AssetRef`, `ClassRef` or `DelegateRef`
  values yet.

## Tests

`tests/OpenScct.CodeGen.Tests`:

- Generator tests on small made-up inputs (`Synthetic.cs`): names and hierarchy, every type mapping, hand-written
  members, shadowing, defaults as differences, consts, replication conditions, the unit manifest and states,
  output identical across runs and with reversed input order, LF only.
- Mapping tests: float literals round-trip every finite float bit for bit (property test), string escapes,
  keywords, type text parsing, the type namer and condition parsing with UnrealScript precedence.
- Generated runtime tests on the tracked output: the class index, hierarchy against the C# types, every factory,
  every property's C# member, decoded defaults, unit ids, `UcManifestCheck` over the runtime, `UcActorState`, and a
  reference evaluation of a replication condition.
