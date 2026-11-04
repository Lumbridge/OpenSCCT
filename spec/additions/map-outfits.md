# Map outfits

OpenSCCT addition, after RE+'s `SCharacterOutfits`. A map can dress either team: new textures on the stock body and
head, or a replacement model. The data is the map's `render/outfits.json` ([SVMap 1.2](../svmap/1.2/outfits.md));
only the client reads it, so an outfit never changes collision, hit zones or vision rules.

## Drawing

- A team with a `model` is drawn with that model instead of its stock mesh. It plays the team's own clips, bound by
  bone name, mirrored clips included (the team's bone mirror table is read through the stock bone names, so the
  model's bone order is free). A model that is missing or cannot be read leaves the stock mesh (the client logs a
  warning).
- Thermal and EMF on a model: its own materials have no vision skins, so the client makes heat and EMF masks for the
  model's UVs from the stock mesh it stands in for (see "Masks for a model" below). Materials the render pack knows
  (a stock mesh worn by the other team) keep their own skins.
- `slots` textures replace the diffuse of the stock slot materials (spy body `Shadw_agent`, head `Shadw_agent_2`;
  merc body `DEF_01_A_Body`, head `DEF_01_A_Face`). The rest of each material stays: the lens and lamp glow under
  their masks, and the slot's heat and EMF skins. On a team with a `model` they apply only where the model draws
  those stock materials: a stock mesh does, a map's own glb normally does not.
- Rain: the wet skins keep the slot's outfit texture as their dry layer, so a wet outfit stays the outfit. A model
  gets wet skins of its own for the materials that lie over a wet stock surface: each keeps its diffuse and glow, with
  wet heat and EMF masks made for its UVs. Pawns draw wet skins in thermal and EMF only (in normal vision every pawn
  keeps its dry skin and the players' cosmetics), so a model's wet skin has no specular layer.
- First person: the merc's arms (`SWeaponTex.1DEF_01` in the guns' view models 1DEF_01, 1DEF_Uzi and 1DEF_shotgun)
  wear the merc outfit when it has a model or a body texture: the model's sleeves and gloves, or the stock suit with
  the body texture. A head texture alone leaves the arms as they are. Texels the arms do not cover keep the view
  model's own texture, and so does its alpha. The pistols' arms are a model of their own whose bones match none of
  the merc's, so they keep their own look.
- Before play the client makes the masks of each outfit model (and in a map with rain its wet skins) and the merc's
  arms, while the map loads, so a pawn's first appearance or first rain does not stop the game.
- The goggle lights hang from the outfit model's goggle bone, moved by its offset in that bone's axes.

## Cosmetics policy

Map outfits win on the body; player cosmetics apply only where the outfit allows (`cosmetics`, per team):

| Policy | Body camo or finish | Light colour | Gun skins |
|---|---|---|---|
| `map` (default) | the outfit (a head texture alone leaves the player's body camo) | the player's | the player's |
| `player` | the player's, where the mesh has a camo for it | the player's | the player's |
| `none` | the outfit | retail | retail |

Under `player` a camo replaces the outfit's slot texture (the camo textures are made from the stock suits); an
outfit model of the map's own has no camo textures, so it keeps its own look, and the merc's first-person arms keep
its sleeves to match. A team without an outfit is not affected by the policy.

## Masks for a model

The client moves textures laid out for one mesh onto another mesh of the same rig while the map loads, in memory
(nothing is written to disk). Both meshes are put in the stock mesh's bind pose: each vertex of the model rides its
bones (matched by name without the rig prefix, so `B L ForeArm` and the view models' `Bip01 L Forearm` are one bone;
a bone the stock rig lacks moves with its nearest ancestor that it has). A triangle with a vertex none of whose bones
match gets no stock point, and a material with none at all gets no masks (the client logs a warning; the editor's
outfit check already requires every bone the team's clips drive). Each texel of a model material's UV layout
(256 x 256) then takes the nearest point of the stock surface, by distance stretched up to twice where the two
surfaces face apart, and reads the stock texture there. Texels between UV islands take a neighbour's point, four
texels out.

- Heat: written in the HEATTEX_Cool form. A cool stock skin's texel is kept; a HEATTEX_Heat skin's (the spy's wet
  head) is divided back at HeatIntensity 1, so it reads a little cool when the rain lowers the heat to 0.5; a stock
  surface without a heat skin gives its flat heat.
- EMF: the stock skin's texel, inverted skins turned back; white where the stock surface has none.
- Wet: the wet skins' heat and EMF of the stock surfaces (a surface without a wet skin, the merc's face, stays dry).
- A model shaped far from the stock suit (a wide helmet, a cape) takes the nearest stock surface, so its masks can
  show stock details in odd places.
