# Fitted cosmetic finishes

Cosmetic names, account IDs, unlock bits and palettes are unchanged. The content
builder fits their paint to geometry and preserves functional materials. The nine
solid suit colours now use `tint_<RGB>` textures instead of a whole-material tint.

All 68 source finishes are original imagegen PNGs under
`content/assets/cosmetics/textures`, including the nine coloured fabric finishes
and legacy pattern IDs. `cosmetic_art.py` reads their RGB artwork directly; no
procedural substitute is used when a source is absent. Missing artwork fails
preparation before cosmetic outputs are written. Every source image and the
skeleton's anatomical region metadata participate in the chars cache key and
ship in the content-tools package. The original generation prompts are retained
beside the textures. Light unlocks still select hues rather than textures.

The skeleton uses frontal and posterior T-pose illustrations. Their skull, torso,
arms, hands, legs and feet are sampled separately onto matching mesh groups using
`skeleton-layout.json`; it never tiles the whole skeleton over the garment.
Surface normals select front or back; arm bones follow shoulder/elbow/wrist joints
without stretching to the width of padded sleeves. The source PNGs are unchanged.
Gold artwork is RGB paint like other finishes; its
reflective response remains the runtime material's masked gold sheen.

`content/svmbuild/fitted.py` reads every primitive of authored sidearms, including
independent vertex buffers and scene transforms. Retail meshes retain their existing
bind-pose reader. The baker needs the actual model; missing geometry is an error,
not permission to draw a pattern across the UV atlas.

Weapon finishes use receiver, barrel, magazine and small-part classification. Retail
lower-frame profiles identify grips welded into the receiver; authored pistols use
their common mechanical coordinate frame. Head coverings, gloves, boots, grip frames
and small parts get the actual skin motif in their own local frames at the same
physical texture scale as the main panels,
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
The manifest binds the original art, fitting code, models and source textures to
the reviewed outputs. Menu thumbnails are staged and installed with the finishes.
Every bake also records its input signature and colour/mask output hashes in
`build-records.json`. Render-only and installation validate these records; rendering
again cannot certify stale outputs after a source change. A partial bake only
finalizes when all unselected outputs still have valid records.
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
