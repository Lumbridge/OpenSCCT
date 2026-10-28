# SVMap 1.1

SVMap is the map format of SVM. Every original `.sdc` map converts to it, and native maps are authored in it. One
map is one folder. 1.1 is a minor version of 1.0: everything a 1.0 reader understands is unchanged, and the additions
below are optional files, optional fields and new collision chunks that 1.0 readers skip.

```
<Map>/
  manifest.json            identity, provenance, per-file digests and group hashes
  sim/                     everything the server reads, in original engine conventions
    gameplay.json          actors with typed properties, map settings, animation notifies, tag index, event edges,
                           mission tree
    classes.json           (1.1, optional) effective class defaults of the classes the actors use
    collision.bin          collision geometry and colliders (binary, chunked)
    traversal.json         geometric events (GEs): ledges, pipes, zip lines, ladders, fences, poles, HOH bars
  render/                  client only
    world.glb              world BSP, static mesh library and the instance table
    materials.json         (1.1) material blend flags, gameplay flags and the material object graph
    lighting.json          (1.1) lights, zones, sky zone, look settings, lightmaps
    fx.json                (1.1) emitters with their sub-emitters, projectors, rain, cloth
    textures/*.png         (1.1) textures and lightmaps named by world.glb and the JSON files
  audio/                   client only (later)
  ui/                      client only
    mapinfo.json           (1.1) map name and briefing texts per game mode
```

The dedicated server loads only `sim/`. The client loads all of it. `fixtures/testbox/` is a complete synthetic
1.0 example; converted maps are 1.1.

## Documents

| File | Content |
|---|---|
| [conventions.md](conventions.md) | units, axes, rotators, transforms, ids, float32 text form, canonical sim hash (unchanged from 1.0) |
| [collision.md](collision.md) | exact byte layout of `sim/collision.bin`, with the 1.1 chunks `CSRC`, `TFLX`, `CYLX` |
| [schemas/](schemas/) | JSON Schemas 2020-12 of `manifest.json`, `sim/gameplay.json`, `sim/traversal.json` and shared definitions |
| [CHANGELOG.md](CHANGELOG.md) | format history |

Schema `$id`s live under `https://svmap.invalid/1.1/`. Test vectors for the float32 text form and the hash are those
of 1.0 (`../1.0/vectors/canonical.json`); the rules did not change. A validator picks the schema folder by the
minor version a file declares, so 1.0 and 1.1 maps validate side by side.

## `manifest.json`

As in 1.0. `formatVersion` is `1.1.x`. `requires` names the shared packs the map was built against, for converted
maps `{"pack": "classes", "sha256": <digest of shared/classes.json>}`.

## `sim/gameplay.json`

- `levelInfo`: the `LevelInfo` actor (`Level.Actors[0]`), with `GEActivationMask` always present (stored, else the
  class default).
- `actors`: every gameplay actor in original level order: `id` (original export name), `class` (`Package.Class`),
  `props` and `src` (`{"export": n}`, the 1-based export index in the original).
- Which actors: a converter writes every actor the simulation can see. It leaves out decoration that only the
  client draws (`StaticMeshActor`, plain `Light`, `AmbientSound`, `Projector`, `AntiPortalActor`, `ERainVolume`,
  soft-body cloth), which lives in `render/` (and, when it collides, in `collision.bin` without an actor id), editor
  only actors (`Camera` viewports, the builder `Brush`), editor ghosts (`bDeleteMe`) and PS2-only actors
  (`Platform = PLF_PSX2_Only`).
- `props` hold the complete stored property bag of the actor, as the original map stores it: only what differs from
  the class defaults. Values are typed as in 1.0. Overlay rules (UE2 serialization): a stored struct overlays the
  class default value member by member, one level deep (each stored member replaces the default's member whole; a
  struct or array inside it is not overlaid further); a stored static array element replaces that element whole; a
  dynamic array replaces the whole value (missing members of its struct elements are zero). Converters also write
  the effective `Tag` of every actor (stored, else the class name).
- References: an actor of the map that is written is `{"$actor": id}`; a render-only or editor actor is
  `{"$asset": "<Map>.<Name>"}` (it lives in the map's `render/` content); a reference to an object that is not in
  the level is `null`. The simulation has no render-only actors, so a loader keeps None for those references (an
  array keeps the element, as None).
- `Base`, `Owner` and `Attached` are serialized engine state like any other prop: a loader sets them as stored, with
  no engine events (no Attach, GainedChild or BaseChange). Bases the engine finds at load are not stored: after the
  level's begin-play, an actor with an `AttachTag` is based (without a BaseChange notification) on the first actor
  in level order whose Tag, else whose id, is that tag, ignoring ASCII case; an actor with `bShouldBaseAtStartup`
  and `bCollideWorld`, physics `PHYS_None` or `PHYS_Rotating` and no `AttachTag` is based on what a short trace down
  finds.
- `mapSettings`: per game mode, keyed by `Story` (GM_Multi), `DiskHunt`, `Deathmatch`, `MercDeathmatch`,
  `Extraction`, `Sabotage`, `Neutralization`: `TimeLimit` (minutes), `ScoreLimit`, `SpiesLives`, `SentriesLives`
  (lives per player), `RespawnTime` (seconds), `Handicap`, `NumDisksMax`, `GameModeId`. From `SMapSettings` in
  `<Map>-i.utc`.
- `animations` (1.1, optional): keyed by the asset path of each skeletal mesh an actor uses (`Mesh`), its default
  animation set: `{"animation": <MeshAnimation path>, "sequences": {<name>: {"frames", "rate", "startFrame",
  "groups", "notifies": [{"time", "function", "object", "class", "props"}]}}}`. `time` is normalized over the
  sequence (0 to 1), `props` the notify object's typed properties (`AnimNotify_Trigger.EventName`,
  `AnimNotify_Sound.Sound`, ...). A sequence of `frames` frames at `rate` played at rate 1 lasts `(frames - 1) /
  rate` seconds as a one-shot.
- Derived indexes, rebuilt and checked by the validator:
  - `tagIndex`: tag -> actor ids in level order (ignoring ASCII case), as in 1.0;
  - `eventEdges`: see below;
  - `missionTree`: as in 1.0, plus `triggers` on a mission entry for the mission's own `Triggers` (Disk Hunt
    missions list their disks there).

### Event edges (1.1 rules)

For each actor in level order (LevelInfo first), its props are walked in document order (object keys sorted by
their ASCII bytes, arrays in order, static array elements by index). Each edge is
`{"from", "via", "event", "to"}` plus an optional `kind`; `via` is the path of the value inside the props
(`Event`, `OpenedEvent`, `DamagedMeshes[0].Event`, `Groups[0].EventGroup[1].Event`, `Events[2]`).

- Fire edges (no `kind`): a string value under a key `Event`, `Events` or ending in `Event` (`OpeningEvent`,
  `OnResetEvent`, `AtResetEvent` ...) that is not empty or `None`. `to`: the actors whose Tag matches, in level order.
- Accept edges (`"kind": "accept"`): an `{"$actor"}` value under a key `DestroyOnEventFrom`, `AlertOnEventFrom`,
  `EventFromActor` or `EventSender`: the owner reacts to its own Tag (`event`) only when the sender is one of `to`.
- Notify edges (`"kind": "notify"`): when the actor's `Mesh` names an entry of `animations`, for each sequence named
  in its `Animations` (or `AnimsFish`) array, in order, for each notify whose props hold an `EventName` that is a
  name: `via` is `Animations[i]`, `event` the EventName, `to` its listeners, plus `seq`, `notify` (the notify object
  path) and `time`, the seconds into the sequence at rate 1, computed in float32 as `(t * frames) / rate`.

Receivers that are `bStatic` never get the event at run time (UE2 dispatch skips them); edges still list them.

Details a reader that rebuilds the edges must follow (the validator compares them exactly):

- Keys match by exact spelling: `event`, `Event2` and `SomeEvents` are not event keys. Tags, events and ids match
  ignoring ASCII case (the names in the shipped maps are all ASCII).
- Under an array the key that counts is the array's own property or member name (`Events[2]`, `TriggerEvent[3]`
  for a static array of names, `DestroyOnEventFrom[0]`); `via` formats static array elements as `[index]` in index
  order and struct members as `.Member`.
- Per actor, the fire and accept edges of all its props come first, in walk order, then its notify edges.
- An accept edge's `event` is the owner's stored `Tag`, `None` when it stores none; `null` and `{"$asset"}` values
  under an accept key make no edge.
- Notify edges need `Mesh` to be an `{"$asset"}` naming an entry of `animations` (exact spelling) and `Animations` or
  `AnimsFish` to be a dynamic array; elements that are not names of a sequence of that set are skipped, and so are
  notifies whose `props` hold no `EventName` name. `notify` is `""` when the notify has no `object`; `time` is `0`
  when `rate` is not positive.
## `sim/classes.json` (1.1, optional)

`{"format": "svm.classes/1", "classes": {<Package.Class>: {"super": ..., "defaults": {...}}}}`: for every class the
written actors use, its effective defaults (what the class default object holds, the English `.int` text applied),
typed like `props`; the super chain is listed with `super` only. An actor's full state is its class defaults
overlaid with its props by the rules above.

- `defaults` lists every property some class of the chain stores, stored zeros included; a property it leaves out is
  zero (false, 0, None, empty) for that class. Struct values are the effective values; static arrays list their set
  elements (`$elements`).
- Native maps may write the table from their engine's class defaults; a loader whose classes already hold these
  defaults only applies what differs.

## `sim/traversal.json`

As in 1.0, plus (optional) `activationMask` at the top (the LevelInfo value), and per GE `flags` (the original flag
word; `0x2` is the editor's selection bit) and `activeAtStart` (mask 0, or a bit shared with `activationMask`).

## `render/world.glb`

glTF 2.0 binary in render space. Scene roots: `World` (the BSP meshes; children carry
`{"svmap": {"role": "bsp", "sky": bool}}`) and `Library` (one node per static mesh variant, named by asset path plus
`#<variant>` for vertex colour, skin and unlit variants, extras `{"svmap": {"role": "libraryMesh", "asset",
"variant"}}`; projector decals and cloth patches are library nodes of the map's own package). The client never
draws `Library` directly. Document extras `{"svmap": {"map", "instances": [...]}}`: one entry per placed mesh
actor, `{"actor", "class", "kind", "mesh" (library node name), "location", "rotation" (UE rotator), "scale",
"hidden", "gameplay"}` plus `prePivot`, `vertexLit`, `unlit`, `ambientGlow`, `cullDistance`, `skyZone`, `skins`,
`keyPos`, `basePos` where they apply, and `softBody` on soft bodies (ESBPatchActor, ESBStripDoorActor: kind, nbU,
nbV, the simulation settings over the class defaults, collSound and shotSound paths, and the saved rest state as
`points`, `uvs`, `fixed` particle indices and `links` [a, b, rest length]; library vertex i and i + N are particle i). Transforms are in UE space; the client converts them through `UeSpace`.
Images are PNG files under `render/textures/` named by relative URI.

## `render/materials.json`, `render/lighting.json`, `render/fx.json`, `ui/mapinfo.json` (1.1)

Client-only JSON, written with the canonical writer but hashed as raw bytes (render group):

- `materials.json`: `materials` (per glTF material name: `index`, exporter `flags`, `source` material path),
  `graph` (every material object reachable from a used material: `class`, typed `props`), `gameplay` (per source
  material: `SurfaceType`, `bClimbable`, `bNoSplitJump`, `bNoStickyCam`, `OcclusionType`, vision flags),
  `additive`, `modulate`, `lightmapped`, `vertexLitMeshes`, `authoredColorMeshes`.
- `lighting.json`: `lights` (exporter fields plus typed `props`), `zones` (fog, ambient, `zoneIndex`, typed
  `props`), `sky`, `look`, `lightmaps`.
- `fx.json`: `emitters` keyed by Emitter actor: `class`, `gameplay`, typed `props`, `emitters` (its sub-emitters
  from its own `Emitters` array: `object`, `class`, typed `props`, `texture`); `spawnParticles` (optional: the
  sub-emitters gameplay objects name in `SpawnableObjects[].SpawnParticle`, keyed by object path, in the same shape);
  `projectors`, `rain`, `patches`.
- `projectors.json` (optional): `projectors`, each texture projector with `location`, `rotation`, `scale`
  (DrawScale x DrawScale3D), `texture`, `size` (USize, VSize), `fov`, `maxTrace`, `fbBlend`, `matBlend`, the projection
  flags, `uvAnim` and `flipbook` when animated. A client that reads it leaves out the `projectorOf` instances.
- `lightbeams.json` (optional): `beams` (lights with UsesLightBeam and a LightBeamMesh: placement, EchelonRange,
  spot size, NearPlaneRatio, LightBeamFalloffScale, LightBeamInitialAlpha, brightness, colour, on state) and
  `meshes` (per beam mesh asset: `positions` flat xyz in mesh space, `indices`).
- `mapinfo.json`: the `SMapSettings` strings and `modes` (per mode name, its briefing texts).

## Versioning

SemVer on `formatVersion`. Patch: clarifications only. Minor: new optional fields, new optional files, new
collision chunks (readers skip unknown chunks); schemas move to a new `1.<minor>` folder. Major: anything a 1.x
reader would misread.
