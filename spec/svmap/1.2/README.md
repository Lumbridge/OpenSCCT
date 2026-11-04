# SVMap 1.2

SVMap is the map format of SVM. Every original `.sdc` map converts to it, and native maps are authored in it. One
map is one folder. 1.2 is a minor version of 1.1: every 1.1 file reads as before, and a 1.1 reader can load a 1.2
map. 1.2 adds:

- a map's own identity, in optional client files that 1.1 readers skip: its name, dossier, authors, recommended
  players, tags, numbered places and pictures (`ui/identity.json`, `ui/pictures/`), and translations of its texts
  (`ui/text/<lang>.json`);
- player-count rules on actors and win targets by team size (`sim/match.json`, [match.md](match.md)), and the teams'
  outfits (`render/outfits.json`, [outfits.md](outfits.md));
- OpenSCCT's native conveyor actor, `Svm.Conveyor` ([conveyors.md](conveyors.md)).

A 1.1 reader cannot play every 1.2 map as authored: a server that does not read `sim/match.json` plays every actor at
every player count, and a reader without `Svm.Conveyor` keeps conveyors as unported actors (their built look draws
and collides, nothing moves). Hosts serve 1.2 maps on builds that read 1.2. Facts a server needs stay in `sim/` and
`manifest.json`. The 1.1 additions over 1.0 are described below as they were.

```
<Map>/
  manifest.json            identity, provenance, per-file digests and group hashes
  sim/                     everything the server reads, in original engine conventions
    gameplay.json          actors with typed properties, map settings, animation notifies, tag index, event edges,
                           mission tree
    classes.json           (1.1, optional) effective class defaults of the classes the actors use
    collision.bin          collision geometry and colliders (binary, chunked)
    traversal.json         geometric events (GEs): ledges, pipes, zip lines, ladders, fences, poles, HOH bars
    rain.json              (1.1, optional) the rain volumes' boxes, which make pawns wet
    match.json             (1.2, optional) player-count rules on actors and win targets by team size
  render/                  client only
    world.glb              world BSP, static mesh library and the instance table
    materials.json         (1.1) material blend flags, gameplay flags and the material object graph
    lighting.json          (1.1) lights, zones, sky zone, look settings, lightmaps
    fx.json                (1.1) emitters with their sub-emitters, projectors, rain, cloth
    textures/*.png         (1.1) textures and lightmaps named by world.glb and the JSON files
    chars/<Package>/       (1.1, optional) the skeletal meshes the map's actors draw (SAnimatedMesh props, DT_Mesh
                           fish) as the shared chars lay them out: <Mesh>.glb, <Animation>.anim.json, textures/;
                           (1.2) also the outfit models the map ships
    outfits.json           (1.2, optional) the teams' slot textures, replacement models and cosmetics policy
  audio/                   client only (later)
  ui/                      client only
    mapinfo.json           (1.1) map name and briefing texts per game mode, in the map's base language
    identity.json          (1.2, optional) name, dossier, authors, version, recommended players, tags, places and the
                           list of pictures
    pictures/              (1.2, optional) the pictures identity.json names: menu, loading (one or more per side),
                           briefing plan, banner. PNG or JPEG, sRGB, no text drawn in them
    strategicmap/floor<n>.png  (1.1, optional) the strategic map's picture of floor n (LevelInfo.MapFloors order; the
                           heights and the snapshot camera are in sim/gameplay.json levelInfo)
    text/<lang>.json       (1.2, optional) a translation of the client texts and of the actors' localised strings
```

The dedicated server loads only `sim/` and `manifest.json`. The client loads all of it. `fixtures/testbox/` is a
complete synthetic 1.0 example. A map declares `formatVersion` `1.2.x` in `manifest.json`, `sim/gameplay.json` and
`sim/traversal.json` when it uses something new in 1.2 (`ui/identity.json`, `ui/text/`, `sim/match.json`,
`render/outfits.json` or a `Svm.Conveyor` actor); other maps stay 1.1. Converted maps are 1.1 unless the original
carries one of the RE+ families that convert to a 1.2 file; maps made in the map editor are 1.2 when they use one of
these.

## Documents

| File | Content |
|---|---|
| [../1.1/conventions.md](../1.1/conventions.md) | units, axes, rotators, transforms, ids, float32 text form, canonical sim hash (unchanged since 1.0) |
| [../1.1/collision.md](../1.1/collision.md) | exact byte layout of `sim/collision.bin`, with the 1.1 chunks `CSRC`, `TFLX`, `CYLX` (unchanged in 1.2) |
| [match.md](match.md) | `sim/match.json`: rules, holds, win targets, how RE+ `SMatchSizeRules` converts |
| [outfits.md](outfits.md) | `render/outfits.json`: slots, models, goggles, cosmetics policy, how RE+ `SCharacterOutfits` converts |
| [conveyors.md](conveyors.md) | `Svm.Conveyor` actors: props, class table, collision and `world.glb` instances |
| [schemas/](schemas/) | JSON Schemas 2020-12 of `manifest.json`, `sim/gameplay.json` (with `Svm.Conveyor`'s props), `sim/traversal.json`, `sim/match.json`, `render/outfits.json`, `ui/identity.json`, `ui/text/<lang>.json` and shared definitions |
| [CHANGELOG.md](CHANGELOG.md) | format history |

Schema `$id`s live under `https://svmap.invalid/1.2/`. Test vectors for the float32 text form and the hash are those
of 1.0 (`../1.0/vectors/canonical.json`); the rules did not change. A validator picks the schema folder by the
minor version a file declares, so 1.0, 1.1 and 1.2 maps validate side by side. `sim/match.json` and
`render/outfits.json` declare none: they follow the manifest's, and a 1.1 map has no schema for them.

## `manifest.json`

As in 1.0. `formatVersion` is `1.2.x`. `requires` names the shared packs the map was built against, for converted
maps `{"pack": "classes", "sha256": <digest of shared/classes.json>}`. `source.kind` is `converted` (from an original
`.sdc`), `authored` (made in a map editor) or `synthetic` (generated test levels). Converted maps, and authored maps
with at least one playable mode, are maps players can host; synthetic ones are not. `title` is the map's name in its
base language: for a map with `ui/identity.json`, the same string as `identity.name`, so a server that has only
`sim/` and the manifest can still name the map.

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
- Classes a map defines (1.2): a map package may carry its own UnrealScript classes (RE+ tools compile into the map,
  map makers added scripts). Converters decode them from the package itself, so their actors are written with full
  props like any other. Their entries carry `"mapScript": true` and their defaults like any other class. OpenSCCT
  loads such an actor as an inert actor (`UnportedActor`) that keeps its class path, its record with every prop and
  its class defaults; none of its script runs. The RE+ families with a native form are not written as actors:
  `SMatchSizeRules` becomes `sim/match.json`, `SCharacterOutfits` becomes `render/outfits.json`.
- The table need not list `Svm.Conveyor` (1.2); a reader that finds no entry uses the class's own defaults
  ([conveyors.md](conveyors.md)).

## `sim/match.json` (1.2, optional)

`{"format": "svm.match/1", "rules": [...], "win": [...]}`: player-count rules on any actor (`players` range,
`outside` and `inside` states, mover key positions 0 to 7) and win targets by team size, 1v1 to 12v12. It is in the
sim hash, so clients and server agree on it. Details: [match.md](match.md).

## `Svm.Conveyor` actors (1.2)

Path conveyors that carry pawns and dropped objects, written in `sim/gameplay.json` like any other actor, with their
built look in `sim/collision.bin` and `render/world.glb`. Details: [conveyors.md](conveyors.md).

## `sim/rain.json` (1.1, optional)

`{"format": "svm.rain/1", "volumes": [{"name", "location", "scale", "size", "falloff"}]}`: every ERainVolume of the
map (also drawn from `render/fx.json` rain), written only when the map has one. `location` is the floor of the box,
`scale` DrawScale x DrawScale3D, `size` m_RainSize, `falloff` m_RainFalloff; the box is (-X, -Y, 0)..(X, Y, Z) of
size x scale from location. A pawn whose location is inside a box is in that rain volume. A reader without the file
may take the same values from `render/fx.json` rain.

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
`keyPos`, `basePos` where they apply (an actor whose mesh has UseVertexColor carries its AmbientGlow folded into
its vertex colours and no `ambientGlow`), and `softBody` on soft bodies (ESBPatchActor, ESBStripDoorActor: kind, nbU,
nbV, the simulation settings over the class defaults, collSound and shotSound paths, and the saved rest state as
`points`, `uvs`, `fixed` particle indices and `links` [a, b, rest length]; library vertex i and i + N are particle i). Transforms are in UE space; the client converts them through `UeSpace`.
Images are PNG files under `render/textures/` named by relative URI.

## `render/outfits.json` (1.2, optional)

`{"format": "svm.outfits/1", "spy": {...}, "merc": {...}}`, client only: per team the body and head slot textures, a
replacement model (the map's glb under `render/chars/<Map>/` or a stock mesh) with its goggle placement, and the
cosmetics policy. The server never reads it. Details: [outfits.md](outfits.md).

## `render/materials.json`, `render/lighting.json`, `render/fx.json`, `ui/mapinfo.json` (1.1)

Client-only JSON, written with the canonical writer but hashed as raw bytes in the group of their folder (`render` or
`ui`; the 1.1 text put all of them in the render group, which the hash code never did):

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
- `mapinfo.json`: the `SMapSettings` strings and `modes` (per mode name, its briefing texts), in the language
  `identity.json` names (English when there is no identity). `LegendMapInBrief` stays for the original game; a map with
  `identity.json` `places` lists them in it. `MapName` is the original's upper-case name; readers show the identity's
  name when there is one.

## `ui/identity.json` (1.2)

Client JSON, written with the canonical writer and hashed in the `ui` group, so renaming a map, swapping a picture or
adding a translation never changes `hashes.sim`. Every field is optional; a reader takes what it finds and ignores
what breaks a rule below, never failing the map. Schema: [schemas/identity.schema.json](schemas/identity.schema.json).

```json
{
 "authors": ["Ryan"],
 "description": "A theme park after closing time. Spies cross three rides to reach the control tower.",
 "dossier": {"summary": "A theme park after closing time.", "when": "9 October 2026, 2:00 AM", "where": "Lyon, France"},
 "family": "Moonlight Park",
 "format": "svm.identity/1",
 "language": "en",
 "name": "Moonlight Park",
 "pictures": {
  "briefing": "pictures/briefing.png",
  "briefingProjection": "snapshot",
  "generated": ["briefing"],
  "loading": {"merc": ["pictures/loading-merc.png"], "spy": ["pictures/loading-spy.png", "pictures/loading-spy-2.png"]},
  "menu": "pictures/menu.png"
 },
 "places": [
  {"at": [1200.0, -340.0, 64.0], "floor": 0, "name": "Ticket hall", "number": 1},
  {"at": [-2210.0, 880.0, 512.0], "floor": 1, "name": "Control tower", "number": 2}
 ],
 "players": {"recommended": 6},
 "tags": ["3v3", "night", "outdoor"],
 "variant": null,
 "version": "1.0.0"
}
```

| Field | Rules |
|---|---|
| `name` | 1 to 48 characters. The name menus show, mixed case, with the variant word when there is one ("Enhanced Aquarius"). Pages that show names upper case, as the original's do, upper-case it. |
| `family`, `variant` | The name without the variant word, and the word ("Aquarius", "Enhanced"); lists sort by `family`. Absent: the reader splits `name` with the shared catalogue's variant words. |
| `description` | At most 300 characters: one or two plain sentences for map browsers and the content index. Absent: the dossier's `summary`. Never markup. |
| `dossier` | `{when, where, summary}`: when and where the map is set, and one line about it. `SMapSettings.LegendMap` is made from it (`when|where||summary`, the shape of the stock files). |
| `authors` | People or teams, in credit order. |
| `version` | The author's version of the map (SemVer recommended), independent of any pack version. |
| `language` | BCP 47 primary tag of the texts in `identity.json`, `mapinfo.json` and the actors' localised properties in `sim/`; `en` when absent. |
| `players` | `{recommended}`: the lobby size (1 to 24, `LobbySetup.MaxMaxPlayers`) the map plays best at. The range of lobby sizes a map supports is gameplay (its match-size rules, `sim/match.json`) and lives in `sim/`, never here. Absent: 6 for maps tagged `3v3` (or in a shared `3v3` playlist), else 4. |
| `tags` | Lowercase `[a-z0-9-]{1,24}`, unique. Known tags: `3v3` (the 3v3 filter lists the map; also implied by `players.recommended` of exactly 6), `1v1`, `competitive`, `casual`, `small`, `large`. Others are shown and searchable. A map never names its own playlists: playlists are kept by whoever curates them (the shared catalogue, the content index, a server's config). |
| `places` | `{number, name, at, floor?}`: the numbered areas of the briefing plan and the strategic map. `number` 1 to 99, unique; `at` a UE-space point, the label's anchor; `floor` the `LevelInfo.MapFloors` index (absent: the floor of `at`'s height). Readers draw the numbers over the strategic map's floors and over a briefing plan whose `briefingProjection` is `snapshot`; `mapinfo.json` `LegendMapInBrief` lists them. |
| `pictures.menu` | The map list picture. Square, at least 512 x 512 (1024 x 1024 recommended). |
| `pictures.loading.spy`, `.merc` | The loading pictures of each side, up to 8 each. Square, 1024 x 1024 recommended. With several, a reader shows one per load (or per session). |
| `pictures.briefing` | The lobby's plan of the map. Square; transparent outside the map allowed. |
| `pictures.briefingProjection` | `snapshot` when the plan was drawn through `levelInfo.SnapshotCamera` exactly as the strategic map's floors are, so a reader can project places onto it; otherwise absent or null, and no numbers are drawn on it. |
| `pictures.banner` | Optional 16:9 picture (1920 x 1080 recommended) for wide layouts such as map browsers. OpenSCCT reads it but its menus do not show it yet. |
| `pictures.generated` | Slots (`menu`, `loading`, `briefing`, `banner`) whose picture a tool drew, so tools know they are not art. |

Pictures: paths are relative to `ui/`, inside `ui/pictures/`, ASCII, no `..`; files are PNG (any alpha) or JPEG,
sRGB, at most 2048 pixels a side and 4 MiB; no text, numbers or logos drawn in (readers draw names and numbers, so
they stay crisp and can be translated). Every picture is listed in the manifest's `files`. A reader ignores a picture
outside `ui/pictures/`, not named `.png`, `.jpg` or `.jpeg`, or over 4 MiB.

Readers resolve each value in this order (the shared catalogue holds Reloaded's names and playlists and the pictures
converted from the original's `-i.utc` packages; `catalog/entry.json` is the older side file of a downloaded map,
outside the spec):

| Value | Order |
|---|---|
| Name | `identity.name` → the shared catalogue's name → `catalog/entry.json` → title-cased `MapName` → `manifest.title` → the map id |
| Name shown upper case | upper-cased `identity.name` → `MapName` → the map id |
| Sort name, variant word | `identity.family`/`variant` → the name split with the shared variant words |
| Recommended players | `identity.players.recommended` → 6 when tagged or listed `3v3` → 4 |
| Menu, loading and briefing pictures | `identity.pictures` → the shared catalogue → `catalog/entry.json`; then for loading, the other side's, then the menu picture; for the briefing plan, the mercs' loading picture (the original's `GetLevelBrief` rule) |
| Hostable | the original's host list → `source.kind` `converted`, or `authored` with at least one playable mode |

## `ui/pictures/` (1.2)

The files `identity.json` names, in the `ui` hash group. Suggested names: `menu.png`, `loading-spy.png` (then
`loading-spy-2.png`...), `loading-merc.png`, `briefing.png`, `banner.jpg`. JPEG keeps 1024 x 1024 loading pictures
near 200 KiB where PNG can take 1 to 2 MiB.

## `ui/text/<lang>.json` (1.2)

One file per extra language; `<lang>` is a BCP 47 primary tag (`fr`, `de`, `es`, `it`, `pl`...). Client JSON in the
`ui` hash group, so lobbies with players in different languages still match. It overrides the base texts key by key;
anything missing falls back to the base language. Schema: [schemas/text.schema.json](schemas/text.schema.json).

```json
{
 "actors": {"Match": {"Description": "...", "DescriptionDEF": "..."}},
 "format": "svm.text/1",
 "identity": {"description": "...", "dossier": {"summary": "..."}, "name": "...", "places": {"1": "...", "2": "..."}},
 "mapinfo": {"ExpliMapInBriefSentry": "...", "ExpliMapInBriefSpy": "...", "LegendMap": "...",
             "modes": {"Story": {"ExplicationModeSpy": "..."}}}
}
```

- `actors`: per actor id, per localised property name (flagged localised in the class table), the translated string;
  array elements as `Name[i]`. A client applies them to the actors in memory after the sim hash is checked, never on
  disk.
- `mapinfo.MapName` is not accepted (the original does not localise it); `identity.name` is.
- Localised actor properties in `sim/gameplay.json` hold the map's base language; translations live only here.
- Readers use these files once the game has a language setting; until then every map shows its base language.

## Versioning

SemVer on `formatVersion`. Patch: clarifications only. Minor: new optional fields, new optional files, new
collision chunks (readers skip unknown chunks); schemas move to a new `1.<minor>` folder. Major: anything a 1.x
reader would misread.
