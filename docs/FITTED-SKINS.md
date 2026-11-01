# Fitted cosmetic finishes

Cosmetic names, account IDs, unlock bits and palettes are unchanged. The content
builder fits their paint to geometry and preserves functional materials. The nine
solid suit colours now use `tint_<RGB>` textures instead of a whole-material tint.

`content/svmbuild/fitted.py` reads every primitive of authored sidearms, including
independent vertex buffers and scene transforms. Retail meshes retain their existing
bind-pose reader. The baker needs the actual model; missing geometry is an error,
not permission to draw a pattern across the UV atlas.

Weapon finishes use receiver, barrel, magazine and small-part classification. Retail
lower-frame profiles identify grips welded into the receiver; authored pistols use
their common mechanical coordinate frame. Head coverings, gloves, boots, grip frames
and small parts get the actual skin motif in their own local frames and scales,
including nebulae, flames and camouflage—not solid-colour substitutes. Original
weave, grip and moulded detail is retained. Boots include the shafts weighted to calf bones.
Displays, exposed skin and equipment markings retain their original material.
Atlas exclusions are for the current retail
UV layouts and must be reviewed when those layouts change. Authored arm bone names
are mapped separately so fingers cannot be classified as sleeves.

Each colour texture has a `.mask.png` companion. The world shader uses this mask for
cosmetic brightness and sheen, so a golden receiver does not make its rubber grip
reflect gold. Old packs without masks retain the previous shader behaviour.

## Build and review without touching active content

From the repository root:

```powershell
python tools/assets/preview_fitted_skins.py --workers 4
```

This stages full-size textures and masks under `local/fitted-skins-review`, renders
both sides of all 13 reference models, and writes `index.html`, readable contact-sheet
pages and a hash manifest. `--cap 512` is available for faster drafts; drafts cannot
be installed. A coverage audit checks every model/finish against the original,
including the spy's head and full boots and weapon grip/small-part regions.
Run only one writer against a staging directory at a time.
`--refinish-parts` updates these secondary regions of an existing full bake; its
output is tested against a complete bake. It does not change the main motif.

The `fitted-skins` Godot smoke exercises every non-light catalog item through the
real Armoury model/material renderer and requires fitted masks. To keep this isolated,
give `--svm-content` an **absolute** path to a staging content root containing
`shared/chars`, including original models, their textures, and the staged finishes.
`SVM_PLAY_SHOTS` enables numbered screenshots during a windowed run.

After visually reviewing the previews and running the smoke:

```powershell
python tools/assets/preview_fitted_skins.py --apply-reviewed
```

Installation verifies source, model and output hashes, then backs up existing files
before replacing only the generated cosmetic PNGs and masks. The backup records new
files separately. It does not change geometry, animations, unlocks or release packs.

Orthographic sheets validate placement and material boundaries. Godot previews also
exercise the finish shader; gameplay lighting, animation and first-person framing
remain separate visual checks.
