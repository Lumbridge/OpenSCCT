# Map outfits

OpenSCCT addition, after RE+'s `SCharacterOutfits`. A map can dress either team: new textures on the stock body and
head, or a replacement model. The data is the map's `render/outfits.json` ([SVMap 1.2](../svmap/1.2/outfits.md));
only the client reads it, so an outfit never changes collision, hit zones or vision rules.

## Drawing

- A team with a `model` is drawn with that model instead of its stock mesh. It plays the team's own clips, bound by
  bone name, and is drawn warm in thermal vision like the stock pawns. A model that is missing or cannot be read
  leaves the stock mesh (the client logs a warning).
- `slots` textures replace the diffuse of the stock slot materials (spy body `Shadw_agent`, head `Shadw_agent_2`;
  merc body `DEF_01_A_Body`, head `DEF_01_A_Face`). The rest of each material stays: the lens and lamp glow under
  their masks, and the slot's heat and EMF skins.
- Rain: the wet skins keep the slot's outfit texture as their dry layer, so a wet outfit stays the outfit.
- The goggle lights hang from the outfit model's goggle bone, moved by its offset in that bone's axes.

## Cosmetics policy

Map outfits win on the body; player cosmetics apply only where the outfit allows (`cosmetics`, per team):

| Policy | Body camo or finish | Light colour | Gun skins |
|---|---|---|---|
| `map` (default) | the outfit | the player's | the player's |
| `player` | the player's, where the mesh has a camo for it | the player's | the player's |
| `none` | the outfit | retail | retail |

Under `player` a camo replaces the outfit's slot texture (the camo textures are made from the stock suits); an
outfit model has no camo textures, so it keeps its own look. A team without an outfit is not affected by the policy.

## Not yet drawn

- Heat and EMF on an outfit model use its materials' names to find vision skins; a model's own materials have none,
  so in thermal and EMF it shows its plain warm look rather than the stock suits' masks.
- An outfit model gets no wet skin in the rain.
- The first-person view keeps the stock arms and textures.
