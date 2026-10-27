# OpenScct.Content

Reads the simulation side of an SVMap folder (`spec/svmap/1.0`, `spec/svmap/1.1`): `manifest.json` and `sim/`, which
is all a dedicated server needs, and turns a map into OpenScct.Core actors. Pure C#, no Godot, deterministic (the
banned-API analyzer from `build/Sim.props` applies).

```csharp
SvMap map = SvMap.Load(@"path\to\SteeE");          // manifest, sim files, sim hash check, id checks
var world = new World(matchSeed, ENetMode.NM_DedicatedServer);
MapLoadResult loaded = new MapLoader().Load(world, map);   // Core and Engine classes; markers for the rest
Console.WriteLine($"{loaded.KnownCount} created from C# classes, {loaded.UnknownCount} markers");

// The game's registry: every generated class of Core, Engine and SBase, then the hand ports that replace some.
ActorClassRegistry registry = ActorClassRegistry.Create(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);
DeviceClasses.Register(registry);
MapLoadResult full = new MapLoader(registry).Load(new World(matchSeed), map);
```

## Namespaces

- `OpenScct.Content`
  - `SvMap.Load(root, SvMapLoadOptions?)`: the manifest, `GameplayDoc`, `ClassTable` (null without
    `sim/classes.json`), `CollisionData` and `TraversalDoc` of one map, plus `SimHash` and `SimFiles` computed from
    disk. It throws `SvMapFormatException` (with `File`) when the sim hash differs from the manifest, naming the
    differing file.
  - `SvMapHash`: `FileDigest`, `GroupListing`, `GroupHash`, `ComputeSimHash`, `HashMap`, `MapFiles`,
    `GroupFiles`. These follow the "Canonical sim hash" rules in `conventions.md` exactly.
  - `SvMapManifest`: `Parse` and `Load`. Members:
    - `Files`, `Hashes`, `SimHash`, `Source`, `Requires`.
    - `DifferingFiles(other, group)` for the handshake.
    - `Verify(root)`.
  - `GameplayDoc`, the content of `sim/gameplay.json`:
    - Actors and settings: `LevelInfo`, `Actors`, `AllActors` (in `Level.Actors` order), `GEActivationMask`,
      `MapSettings` / `GetModeSettings`, `AssetPaths`, `FormatVersion` and `FormatMinor`.
    - 1.1 animations: `Animations`, `FindAnimationSet(meshPath)` (`AnimationSet`, `AnimSequenceInfo` with
      `OneShotSeconds`, `AnimNotifyInfo` with `SecondsInto` and `NotifySeconds`).
    - Derived indexes as stored: `TagIndex` / `ActorsWithTag`, `EventEdges` (`EventEdge` with the 1.1 `Kind`,
      `EdgeKind`, `Seq`, `Time`, `Notify`), `MissionTree` (`MissionNode.Triggers` in 1.1).
    - Lookup: `FindActor(id)`. Ids, tags and events ignore ASCII case.
    - Checks: `CheckDerived()` rebuilds the derived indexes with the rules of the document's minor version (1.0:
      `Event` only; 1.1: fire, accept and notify edges, mission triggers) and names the first difference;
      `CheckReferences()` finds dangling `$actor` ids. `ComputeTagIndex`, `ComputeEventEdges`, `ComputeMissionTree`
      give the rebuilt indexes; `IsEventKey` is the 1.1 key rule.
    - Writing: `ToJson()`, `ToJson(rebuildDerived: true)` and `WithDerived()` (the pipeline's `with_derived`).
  - `ClassTable`, the content of `sim/classes.json`: `Classes` (`ClassEntry`: `Path`, `Super`, `Defaults`), `Find`,
    `Chain`, `IsA`, `DefaultsOf`, `Effective(record, name)` and `EffectiveProps(record)` (defaults overlaid with the
    record's props), `Overlay(default, stored)` (the 1.1 overlay rules), `Check(gameplay)`, `ToJson()`.
  - `ActorRecord` (`Index`, `Id`, `Class`, `ClassPackage`, `ClassName`, `Props`, `SourceExport`, `Tag`, `Event`,
    `Location`, `Rotation`). `PropBag` is a typed property bag keyed by UnrealScript name, ignoring case.
    - Typed getters take a default: `GetInt`, `GetByte`, `GetFloat`, `GetBool`, `GetString`, `GetName`,
      `GetEnum`, `GetVector`, `GetRotator`, `GetStruct`, `GetArray`, `GetStaticArray`.
    - References: `GetActorRef` and `GetActor` resolve lazily, so forward references work. `GetAsset` returns an
      `AssetRef`, and `AssetResolver<T>` loads and caches assets on first use. `GetClass` returns the class name.
    - Each value is a `PropValue`, whose kind is one of the `PropKind` values.
  - `TraversalDoc` and `GERecord`: `Kind` (`GEKind` POLE 1 ... ZL 64), `Points`, `Mask`, `Platform`, `Source`,
    and `IsActive(activationMask)`.
- `OpenScct.Content.Collision`:
  - `CollisionReader.Read(bytes, strict)` reads every 1.0 chunk. Unknown chunks (the 1.1 `CSRC`, `TFLX`, `CYLX`)
    are skipped by size and kept in `UnknownChunks`. Every index and range is checked.
  - `CollisionWriter.Write(data)` writes output byte-identical to the pipeline's writer.
  - `CollisionData` has `ImmutableArray`s of record structs (`CollisionTriangle`, `CollisionHull`, `Collider`...)
    and span helpers (`TrianglesOf`, `HullsOf`, `PlanesOf`, `CornersOf`...). `Collider.Geometry` applies the
    hulls, then BSP, then triangles rule. `TryGetCollisionBound(node)` gives a BSP node's collision bound: in
    converted maps the original LeafHulls region behind the node, a hull of no collider.
- `OpenScct.Content.Json`:
  - `SvJson.Parse` is the strict reader. It rejects a BOM, duplicate keys, NaN, comments and trailing commas. It
    rounds float tokens straight to binary32 and keeps integer tokens as text.
  - `Float32Text` produces the float32 text form.
  - `CanonicalJson`:
    - `Canonicalize` and `ToCanonicalBytes` produce the canonical form.
    - `ToFileBytes` produces the on-disk form, byte-identical to `svmbuild.canonical.dump_file`.
- `OpenScct.Content.Loading`: see "Loading a map".

## Loading a map

`MapLoader.Load(world, map)` (or `Load(level, gameplay, classes)`) builds the level the way a UE2 map load does:

1. **Classes.** The level's LevelInfo takes the map's LevelInfo record. Every other record becomes the C# class the
   `ActorClassRegistry` names, else an `UnportedActor` marker (`ClassPath`, `Record`, `ClassDefaults`,
   `GetEffective(name)`).
   - `ActorClassRegistry.CreateDefault()` registers every concrete generated actor class of Core and Engine
     (`Engine.PlayerStart`, `Engine.Mover`, `Engine.BlockingVolume`...); `Create(packages)` and
     `RegisterPackage(package)` add others (SBase from OpenScct.Game); `Register<T>(classPath)` replaces one with a port
     or a stand-in. `IsGenerated` and `GeneratedClass` tell which.
2. **Class defaults.** A new object holds its generated defaults (SetDefaults). Where the map's class table differs
   for the C# class created, the table wins: values that change a member are applied, and script properties the
   table leaves at zero but the object does not hold at zero are reset. The difference is computed once per class
   and C# type. For a generated class it is empty on every shipped map (the generator and the pipeline decode the
   same defaults); for a marker it is the Actor fields its class sets (collision, flags, physics...).
   `ApplyClassDefaults = false` turns this off. Identity and placement (`Tag`, `Location`, `Rotation`, `Base`,
   `Owner`, `Level`, `Region`...) never come from the table.
3. **Props.** The record's props are copied onto public fields and settable properties by UnrealScript name, most
   derived declaration first, plus the original names of members the generator renamed.
   - Types: bool, int, byte, float, string, `Name`, enums, structs, static arrays (also `FixedArrayN` inside
     structs), `List<T>`, actor references, `OpenScct.Core.Uc.AssetRef` (`$asset`), `ClassRef` (`$class`), and None for
     `DelegateRef` and other object references.
   - Overlay (spec/svmap/1.1): a struct overlays the member one level deep, a static array element replaces that
     element, a dynamic array replaces the whole value.
   - Members holding actor references (also inside structs and arrays) are applied once every actor exists. A
     reference to a render-only actor (`{"$asset": "Map.Name"}`) or a missing one loads as None.
4. **Base and Owner.** A stored `Base` or `Owner` is serialized state: it is set with `Actor.SetLoadedBase` and
   `SetLoadedOwner`, without Attach, GainedChild or BaseChange events, and the base's `Attached` list follows.
   `Level` must name the LevelInfo.
5. **Begin play.** Actors are spawned in level order between `Level.BeginLoad` and `Level.EndLoad`, so their
   begin-play events run as a map load.
6. **Startup bases.** `MapLoadResult.AttachStartupBases()`: after begin play, each actor with an `AttachTag` is based
   (without BaseChange) on the first actor in level order whose Tag, else whose map id, is that tag (UE2 load rule;
   `AttachTagBases` lists the results). The loader calls it when it owns the load; a caller that began the load
   itself calls it after `EndLoad`. `StartupBaseCandidates()` and `FindStartupBases(findBase)` run the
   `bShouldBaseAtStartup` floor search with a trace the caller supplies (no shipped map has a candidate).

`MapLoadResult` has `Actors` (indexed like `AllActors`), `FindActor`, `ActorOf`, `RecordOf`, `Resolve(ActorRef)`,
the counts, `UnknownClasses`, `Classes` (`MapClassReport` per class: count, C# type, kind, whether it has ported
code, class defaults applied and without a member), `UnappliedProps` (`Package.Class.Prop` with actor counts) and
`Unapplied` (`UnappliedProp` with an `UnappliedReason`: `NoMember`, `TypeMismatch`, `RenderOnlyActor`,
`ObjectReference`, `MissingActor`, `WrongActorClass`, `UnportedTarget`, `UnportedMismatch`).

`ClassTableBuilder.Build(classes)` writes a class table from the generated classes (their non-zero defaults), for
native maps and as the loader's reference: a table built this way changes nothing when loaded.

## Tests on the converted maps

`tests/OpenScct.Content.Tests/AllMapsTests.cs` runs every map under `.content/svmap` (or `$SVM_CONTENT/svmap`) and is
skipped when there are none. Per map: the sim hash equals the manifest's, the derived indexes rebuild to the stored
ones, references and the class table resolve, gameplay.json round-trips canonically, BSP collision bounds are hulls
of no collider, and the map loads with the default registry and with the game's, every actor spawned. It writes
`local/content/all-maps-load.md`: actor counts, markers, unknown classes, unapplied props by reason, class default
differences and timings. On the 83 shipped maps the game registry creates every actor from a C# class, applies no
class default (none differs) and leaves only object references (`StaticMeshInstance`, `ZoneEffect`, `Summary`) and
references to render-only actors unapplied; reading, checking and loading all 83 takes about 8 s.

## Notes

- A loader instance caches reflection data and class default differences, and is not thread-safe. The documents
  are read-only once loaded, and lazy reference resolution is safe to share.
- Readers follow SemVer:
  - `formatVersion` must be 1.x and collision major must be 1.
  - Unknown manifest members and unknown collision chunks are ignored, since minor versions only add.
- 1.0 maps (TestBox) have no class table: their actors keep the generated defaults plus their props.
