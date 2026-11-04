# `render/outfits.json` (SVMap 1.2, optional, client only)

What the two teams wear on this map: replacement textures for the body and head slots, a replacement model, and
whose look wins over the players' cosmetics. Only the client reads it; an outfit never changes collision, hit zones,
vision rules or anything else the server simulates. Run-time rules are in
[map-outfits.md](../../additions/map-outfits.md).

```json
{
  "format": "svm.outfits/1",
  "spy": {
    "slots": {
      "body": {"texture": "textures/ShipD_SpyBody_abbc8201.png", "source": "ShipD.CharacterSkins.SpyBody_abbc8201"},
      "head": {"texture": "textures/ShipD_SpyHead_771f4411.png"}
    },
    "cosmetics": "map"
  },
  "merc": {
    "model": {
      "glb": "chars/ShipD/HazmatMerc_91d44bb5.glb",
      "goggles": {"bone": "B Head", "offset": [-0.1, -2.8, -0.8]}
    },
    "cosmetics": "map"
  }
}
```

Each team (`spy`, `merc`) is optional; a team without an entry wears the stock look.

- `slots` (optional): `body` and `head`, each `{"texture", "source"?}`. `texture` is a PNG path relative to
  `render/` (normally under `render/textures/`); it replaces the diffuse of the stock mesh's slot (spy: body
  `SPersoTextures.Shadw_agent`, head `Shadw_agent_2`; merc: body `DEF_01_A_Body`, head `DEF_01_A_Face`) and keeps
  the rest of that material (self-illumination mask, heat and EMF skins). It is laid out on the stock UVs. `source`
  is informational: the original object the texture came from.
- `model` (optional): exactly one of
  - `glb`: a skinned glTF the map ships, relative to `render/` (normally `render/chars/<Map>/<Name>.glb`, laid out as
    the shared chars are), or
  - `stock`: a shared skeletal mesh by asset path (`SPerso.DEF_01`).

  The model plays the team's own clips, matched by bone name, so it must have every bone they drive.
  `goggles` (optional) places the goggle lights: `bone` (default `"B Head"`) and `offset`, UE units in that bone's
  axes, added to the beam's own relative location (default `[0, 0, 0]`). Slot textures apply to a model only where
  it has the stock slot's material.
- `cosmetics` (optional): whose look wins on this team: `"map"` (the default: the outfit on the body; the player's
  light colour and gun skins still apply), `"player"` (the player's body cosmetics where the outfit allows them) or
  `"none"` (no player cosmetics at all on this team: retail lights and guns too).

Every file named here is a file of the map (listed in `manifest.json` like the rest of `render/`). Schema:
[schemas/outfits.schema.json](schemas/outfits.schema.json) (the format id, team, slot and policy names, relative
paths without `..`, and exactly one of `glb` and `stock`). The validator also checks that each texture and glb exists
under `render/`.

## Converted maps

RE+'s `SCharacterOutfits` (and its older names `SCharacterSkins`, `ReloadedCharacterSkins` to
`ReloadedCharacterSkins5`), a class RE+ compiles into the map package, becomes this file; its actor is not written to
`sim/gameplay.json`. When a map keeps several, the newest class wins and the converter notes the others.

- `SpyBody`, `SpyHead`, `MercBody`, `MercHead`: the slot textures, exported to `render/textures/`.
- `SpyModel`, `MercModel`: a skeletal mesh of the map package is exported with its textures to
  `render/chars/<Map>/` (`glb`); a mesh of another package is named as `stock`. `SpyGoggleOffset` and
  `MercGoggleOffset` are the goggle offsets on `B Head`.
- `cosmetics` is written as `"map"`: RE+ has no player cosmetics, so the map's look is what its author saw.
- `ReloadedBotController`, the controller RE+'s Play Level bots used, is dropped with them.
