# SVMap changelog

## 1.0.0 (draft)

- Folder layout: `manifest.json`, `sim/{gameplay.json, collision.bin, traversal.json}`, `render/world.glb`;
  `audio/` and `ui/` reserved.
- Conventions: UE units, left-handed Z up, 65536 rotators and float32 in `sim/`; glTF metres `(x, z, y) / 100` with
  flipped winding in `render/`; original export names as ids; float32 text form; canonical sim hash.
- `collision.bin` 1.0: file header `SVCL` 1.0 and chunks `STRS VERT TRIS TFLG PLNS HULL BSPN COLL CYLS ZONE`, all
  chunk layouts version 1. `BSPN` is provisional until the map converter decodes the original `LeafHulls`.
- JSON Schemas 2020-12 for `manifest.json`, `sim/gameplay.json` and `sim/traversal.json`.
- `fixtures/testbox` is the reference example.

## Later versions

1.1.0 adds optional files, fields and collision chunks; see `../1.1/CHANGELOG.md`. Readers of 1.0 read 1.1 maps.
