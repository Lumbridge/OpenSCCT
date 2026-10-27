# SVMap changelog

## 1.1.0 (draft)

Additive: every 1.0 file reads as before, and 1.0 readers skip what is new.

- Converted maps are 1.1. `formatVersion` `1.1.x`; schemas in `spec/svmap/1.1/schemas` with `$id`s under
  `https://svmap.invalid/1.1/`. The float32 text form, canonical JSON and hash rules are unchanged (1.0 vectors).
- `sim/gameplay.json`:
  - which actors a converter writes (gameplay actors; decoration goes to `render/`, editor and PS2-only actors are
    left out) and how a render-only actor is referenced (`{"$asset": "<Map>.<Name>"}`);
  - the overlay rules of stored structs, static arrays and dynamic arrays over the class defaults;
  - optional `animations`: the animation sets of the skeletal meshes actors use, with notifies;
  - `eventEdges` 1.1 rules: fire edges from every event-name property at any depth (`via` is a property path),
    accept edges (`kind: accept`) from sender filters, notify edges (`kind: notify`, `seq`, `time`, `notify`) from
    animation notifies;
  - optional `triggers` on a `missionTree` entry (the mission's own Triggers);
  - `mapSettings` keys named after the game modes (`Story`, `DiskHunt`, `Deathmatch` ...);
  - clarified: overlays are one level deep, how loaders treat render-only references and stored `Base`, `Owner`
    and `Attached`, the load-time `AttachTag` and `bShouldBaseAtStartup` bases, and the exact event-edge details
    (key spelling, array keys, order, accept and notify edge corner cases).
- New optional `sim/classes.json`: effective class defaults of the classes the actors use (clarified: a property
  `defaults` leaves out is zero).
- `sim/traversal.json`: optional `activationMask`, and per GE `flags` and `activeAtStart`.
- `sim/collision.bin`: header minor 1 and the chunks `CSRC` (collider source, asset, collision set, mesh variants,
  trace use), `TFLX` (BSP poly flags and material bits), `CYLX` (cylinder kind and interaction class) after `ZONE`;
  the converter rules for every collider kind; `BSPN` holds the original nodes with their collision bounds: the
  original `LeafHulls`, decoded to `HULL` records that belong to no collider (no longer provisional).
- `render/`: `world.glb` scene roots, library node naming by asset path and the instance table in the document
  extras; new `materials.json`, `lighting.json`, `fx.json` and `textures/*.png`.
- `ui/mapinfo.json`: map name and briefing texts.
- Client-only JSON (`render/`, `ui/`) may use non-ASCII object keys; `sim/` keys stay ASCII. File names stay ASCII
  (a converter escapes other characters as `_uXXXX`).

## 1.0.0 (draft)

- Folder layout: `manifest.json`, `sim/{gameplay.json, collision.bin, traversal.json}`, `render/world.glb`;
  `audio/` and `ui/` reserved.
- Conventions: UE units, left-handed Z up, 65536 rotators and float32 in `sim/`; glTF metres `(x, z, y) / 100` with
  flipped winding in `render/`; original export names as ids; float32 text form; canonical sim hash.
- `collision.bin` 1.0: file header `SVCL` 1.0 and chunks `STRS VERT TRIS TFLG PLNS HULL BSPN COLL CYLS ZONE`, all
  chunk layouts version 1. `BSPN` is provisional until the map converter decodes the original `LeafHulls`.
- JSON Schemas 2020-12 for `manifest.json`, `sim/gameplay.json` and `sim/traversal.json`.
- `fixtures/testbox` is the reference example.
