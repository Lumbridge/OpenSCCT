# SVMap changelog

## 1.2.0 (draft)

Additive in its files: every 1.1 file reads as before, and a 1.1 reader can load a 1.2 map. It cannot play every one
as authored: `sim/match.json` changes the match, and a server that does not read it plays every actor at every player
count; a reader without `Svm.Conveyor` keeps conveyors as unported actors. A host serves 1.2 maps only on a build that
reads 1.2 (the map's sim hash covers `sim/match.json` and the conveyor actors, so clients and server agree on them).

- Maps that use something new in 1.2 are 1.2: `formatVersion` `1.2.x`; schemas in `spec/svmap/1.2/schemas` with
  `$id`s under `https://svmap.invalid/1.2/` (the 1.1 schemas with the new version and `Svm.Conveyor`'s props, plus
  `sim/match.json`, `render/outfits.json`, `ui/identity.json` and `ui/text/<lang>.json`). Other maps stay 1.1. `conventions.md` and `collision.md` are unchanged and stay in
  `spec/svmap/1.1/`.
- New optional `ui/identity.json` (`svm.identity/1`): name, family and variant, description, dossier, authors,
  version, language, recommended players, tags, numbered places and the list of pictures, with the order readers
  resolve each value in. No playlist membership and no player range: playlists belong to their curators, the player
  range to `sim/`.
- New optional `ui/pictures/`: menu, loading pictures per side (several allowed), briefing plan (marked `snapshot`
  when drawn through the snapshot camera, so places can be drawn on it) and banner; PNG or JPEG, no text drawn in.
- New optional `ui/text/<lang>.json` (`svm.text/1`): translations of the identity, `mapinfo.json` and the actors'
  localised strings, outside the sim hash. Localised text in `sim/` is in the map's base language.
- New optional `sim/match.json` (`svm.match/1`): player-count rules on any actor (`players` range, `outside` and
  `inside` states, mover key positions 0 to 7) and win targets by team size, 1v1 to 12v12 (match.md).
- New optional `render/outfits.json` (`svm.outfits/1`): per team the body and head slot textures, a replacement
  model (the map's glb under `render/chars/<Map>/` or a stock mesh) with its goggle placement, and the cosmetics
  policy (outfits.md).
- New native actor class `Svm.Conveyor`: path conveyors that carry pawns and dropped objects, with their built look
  in `sim/collision.bin` and `render/world.glb`; `sim/classes.json` need not list the class (conveyors.md).
- `sim/classes.json`: classes the map package defines itself are decoded from the package and marked
  `"mapScript": true`; their actors are written with full props.
- `manifest.json`: `title` equals `identity.name` when an identity exists; authored maps with a playable mode are
  hostable like converted ones.
- Corrected: `ui/mapinfo.json` is hashed in the `ui` group, not the render group (the hash code always did this).

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
- `sim/rain.json` (optional): the rain volumes' boxes and falloff, so a server without `render/` knows where pawns are
  wet.
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
