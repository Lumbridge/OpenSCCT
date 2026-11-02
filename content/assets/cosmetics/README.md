# Cosmetic source artwork

All textured unlockables now use fresh imagegen artwork: 26 active special and
ladder patterns, 32 distinct map camos, and nine solid-colour textile materials.
The legacy unused Breaker source is also supplied. Skeleton has separate front
and rear anatomy, making 69 source PNGs for 68 builder patterns and 67 currently
used designs. These cover the catalog's 217 suit and weapon cosmetics. The 17
light unlocks remain numeric hue settings and have no source texture.

`textures/` contains the unmodified built-in imagegen outputs. `prompts/` contains
the exact prompt for each image; `manifest.json` records source and prompt hashes.
No API/CLI fallback was used. See `CREDITS.txt` for provenance.

The content builder samples these images in model space, preserves the protected
gear/skin regions and original alpha, and writes fitted textures, finish-lighting
masks and matching menu swatches. It fails on missing source artwork rather than
substituting the former procedural design. Existing item IDs, unlocks and saved
looks are unchanged. `skeleton-layout.json` maps anatomical regions to the source
illustrations; it does not alter their pixels.

Prepare game files rebuilds the character step when source artwork changes. The
content-tools release includes this directory. Source images belong in Git; fitted
retail-derived outputs stay in the ignored `.content/` or `local/` directories.

To stage and review all fitted outputs before local installation:

```powershell
python tools/assets/preview_fitted_skins.py --workers 4 --output local/cosmetics-imagegen-review
python tools/assets/preview_fitted_skins.py --apply-reviewed --output local/cosmetics-imagegen-review
```

The first command creates full-resolution textures, actual-mesh review sheets and
a coverage audit. The second verifies source/output hashes and installs them with
a restorable backup. The `fitted-skins` Godot smoke renders every textured catalog
item through the actual preview materials; gameplay lighting and animation still
need their own visual checks.
