# External asset workflow

## Editable spy and merc bases

`author_characters.py` builds Blender authoring files from the user's locally
converted characters. Their original mesh topology, UVs and silhouette are
retained. This is an authoring upgrade, not newly sculpted replacement geometry.

- Spy: `local/characters/spy/spy-authoring.blend`, 43 game bones,
  95 control/mechanism bones and 339 preserved Action assets.
- Merc: `local/characters/merc/merc-authoring.blend`, 42 game bones,
  93 control/mechanism bones and 144 preserved Action assets.
- `*_CONTROLS` is the artist rig; `*_EXPORT` is the game skeleton. Original
  material names/vision metadata are retained. Editable images live in each
  character's `textures/` directory.
- Pose `CTRL_*` bones for FK. The rig object's `ik_L_hand`, `ik_R_hand`,
  `ik_L_foot`, `ik_R_foot` properties enable the corresponding `IK_*` target and
  `POLE_*` bend controller. Set `controls_active=0` when playing an original
  action directly on the export rig. IK/FK switching does not automatically
  snap targets to the current FK pose.
- Each file has an embedded START HERE text. Widgets/control bones never enter
  the game export. The optional subdivision modifier is off by default.

Build authoring files (regenerates them; save edits elsewhere first):

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python-exit-code 1 --python tools/assets/author_characters.py -- --repo C:/Users/ryans/source/repos/SVM-Rewrite
```

Export model/texture edits while preserving original gameplay animations:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python-exit-code 1 --python tools/assets/export_character.py -- local/characters/spy/spy-authoring.blend local/characters/spy/export/ATT_01.glb
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python-exit-code 1 --python tools/assets/export_character.py -- local/characters/merc/merc-authoring.blend local/characters/merc/export/DEF_01.glb
```

The default export copies original animation tracks/timing exactly; it does not
export edits to those actions. A local `reference.glb` snapshot keeps those
tracks stable across repeated exports/installations. For a new control-rig animation, save a working
copy, key the controls, set the scene frame range and pass `--clip YourClipName`.
This bakes the evaluated deformation skeleton and exports only the new clip.
New clips need a separate gameplay mapping/timing integration before deployment.
One-second hand-reach demonstrations were baked and loaded in Godot for both
characters, with approximately 0.265 m of measured bone movement.

`local/characters/enabled.json` lists enabled local overrides, currently
`["spy", "merc"]`. The chars content step hashes the enabled GLBs and referenced
textures, validates bone names, existing clip names/durations and placement
metadata, then installs them before cosmetic texture generation. No manifest
means standard converted content. Set it to `[]` and rebuild chars to disable.
Only compatible full-character exports belong in `export/`; single-clip
experiments belong elsewhere, such as `demo/`.

The local converted models were backed up under
`local/characters/backups/before-authoring/SPerso` before installation. Source
`.anim.json` files are unchanged. Derived Blender files/textures stay local and
are not committed or included as independent redistributable assets.

Validation: rig rest-position/rotation checks, four IK effector checks per
character, structural export checks, and `compare_character.gd` sampling five
poses from every original clip. The latter compares bone world position,
orientation and clip duration through Godot's runtime importer.

Verified locally on 2026-10-31: 2,415 round-trip poses passed, all 483 original
clips retained with unchanged duration, both spy and merc headless `play-anim`
gameplay smokes passed with zero bots, and ten focused Python tests passed.
The smoke reports note the existing `ChangeSpeed` input-delivery limitation;
this is not a manual full-moveset playtest.

Blender MCP searches/imports assets and edits the authoring scene. The scripts
below turn external sources into repeatable, inspectable staged GLB exports.
Only the explicitly enabled full-character exports above are installed into
the local gameplay content.

## Local integration

This workstation has `blender` and `mixamo` registered in the user Codex MCP
configuration. Their isolated environments and source checkouts are under
`C:/Users/ryans/.codex/integrations/`:

After setup or server-code changes, restart Codex to refresh cached MCP
transports. A fresh protocol client was tested independently of the chat's
cached connection.

- `mcp-for-blender`: version 2.2.6, upstream commit
  `992233bb258975a05eec8b5691465c50c2831e34`.
- `mixamo-mcp`: upstream commit `8bdb0a3ed91fdc1fadc469701e09ecb0bdb7aefd`,
  MCP dependency pinned to 1.30.0. Local changes fix browser cleanup and retain
  Adobe login state separately from the headless automation contexts.
- Blender 5.2.2 LTS, add-on protocol 15. Poly Haven enabled in the saved asset
  workspace; telemetry disabled and server safe mode enabled. The upstream
  checkout omitted `blender_mcp/config.py`; a local disabled-telemetry config
  supplies it. Preserve that patch when updating the integration.

Open `asset-workspace.blend` from the integrations directory in Blender to start
the bridge on localhost:9876. The add-on is enabled in Blender preferences;
Poly Haven is a per-scene checkbox. Only one live Blender should own this port.
Sketchfab and Poly Pizza remain optional and need their own API keys. No paid
generation provider was enabled.

If Mixamo reports an expired login, run its `login.py` using its virtualenv
Python. Sign in in the window, wait for the character page, then close the
window. `session-state.json` is a local credential file, not an asset: never
copy it into the repository. An authenticated session was tested with an X Bot
rifle-walk download; no OpenSCCT character was uploaded.

Upstreams: https://github.com/ahujasid/mcp-for-blender and
https://github.com/V1xel/mixamo-mcp. Asset licences are separate from MCP code.

## Static assets and native-rig animation exports

Save a source `.blend`, `.fbx`, `.gltf` or `.glb` and create a manifest beside it:

```json
{
  "source": "crate.blend",
  "output": "export/crate.glb",
  "objects": ["crate"],
  "source_url": "https://polyhaven.com/a/plastic_crate_03",
  "author": "Pacific Penguin",
  "license": "CC0-1.0",
  "triangle_target": 12000,
  "validation": {"max_triangles": 12500}
}
```

`objects` and `triangle_target` are optional. The triangle target applies per
static mesh; validation checks the total budget. Decimation is intentionally
rejected for skinned meshes. `clip_names` optionally maps input action names
to exported clip names. Run from the repository:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python-exit-code 1 --python tools/assets/export_asset.py -- local/asset-pipeline/crate.json
python tools/assets/validate_glb.py local/asset-pipeline/export/plastic_crate_03.glb
```

The exporter writes a GLB, credits text and a report containing source/output
SHA-256 hashes, Blender version, triangle count, bones, clips and failures.
It packs textures into the GLB. Review material appearance and decimation in
addition to the structural checks. These checks are not a full glTF validator.

## Mixamo retarget pilot

Download FBX with skin at 30 fps, with **In Place** checked. Create a manifest:

```json
{
  "source": "mixamo-walk.fbx",
  "target": "../../.content/shared/chars/SPerso/ATT_01.glb",
  "output": "export/spy-mixamo-walk.glb",
  "clip_name": "MixamoRifleWalkPreview",
  "fps": 30,
  "source_url": "https://www.mixamo.com",
  "author": "Adobe Mixamo",
  "license": "Mixamo terms; local evaluation"
}
```

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python-exit-code 1 --python tools/assets/retarget_mixamo.py -- local/asset-pipeline/walk.json
```

The pilot maps 22 body bones to the spy `B ...` skeleton, aligns anatomical
axes, preserves target proportions, bakes pose keys, retains vertical pelvis
bob and removes horizontal displacement. It exports one clip with its timeline
starting at zero, a review `.blend`, and a report. An explicit `bone_map` can
override the defaults, but the anatomical alignment currently expects the spy
and Mixamo names shown in the script. This is not a universal rig retargeter.

Unmapped fingers, twist bones, root and attachments retain their rest offsets.
Hands therefore need additional work for a precise weapon grip. Inspect feet,
loop seams, posture and contacts before accepting a clip. Source and reference
files are never overwritten. A preview containing the local reference geometry
stays under ignored `local/`; it is not a redistributable asset pack.

OpenSCCT's `SkeletalModel`/`AnimSampler` bind tracks by bone name. Its simulation
also uses `.anim.json` data for animation timing/root motion; replacing a render
GLB does not update that data. This pilot deliberately removes the reference's
simulation-animation pointer from exported metadata. It does not replace
production animation names or modify the simulation. Integrating a replacement
requires matching clip durations/events and validating root motion, contacts,
attachments, vision materials and multiplayer behaviour in-game.

## Verification

```powershell
python -m unittest discover -s tools/assets -p test_validate_glb.py
& tools/godot/4.7.2-stable/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path local/asset-pipeline --script ../../tools/assets/godot_validate.gd -- C:/Users/ryans/source/repos/SVM-Rewrite/local/asset-pipeline/export/plastic_crate_03.glb C:/Users/ryans/source/repos/SVM-Rewrite/local/asset-pipeline/export/spy-mixamo-walk.glb
```

The isolated `local/asset-pipeline/project.godot` only needs `config_version=5`.
The Godot smoke imports the files, generates meshes/skeletons, samples clips,
and checks finite bone transforms. It does not run gameplay.

Pilot sources: Poly Haven `plastic_crate_03` (Pacific Penguin, CC0) and Mixamo
animation `116160902`, Walking while aiming rifle, on X Bot. Both were downloaded
through the installed integrations. The crate was reduced to 12,000 triangles;
a 6,000-triangle trial visibly damaged the lattice and was rejected.
# Weapon reload authoring

The content build applies `content/svmbuild/reload_choreography.py` to the rifle,
Uzi, shotgun and all five merc sidearms. Revision 2 replaces the reload timelines:
independent weapon presentation, hand contacts, magazine extraction/insertion,
slide/bolt handling and recovery. Reference grip offsets and finger shapes are
calibrated from the source clips; the old motion is no longer the underlying
trajectory. A separate dropped magazine permits the old magazine to fall while
the spare approaches. The shotgun has a new visible shell prop, and the revolver
has independent cylinder, loader and support-hand paths with ejected cases.
The familiar primary geometry and credited replacement sidearm models remain.

The pass preserves gameplay clip names/durations, shotgun shell cues and ammo
rules. Each main reload has its own profile; contextual grenade/other reload
clips are preserved. Third-person character reloads are
unchanged. The spy shocker does not have a magazine reload to replace.

Generate editable first-person workspaces after building content:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' -b --python-exit-code 1 --python tools/assets/author_weapon_rigs.py -- --repo .
```

Each `local/weapons/<weapon>/<weapon>-authoring.blend` contains a separate FK
control skeleton, grouped hands/fingers/mechanisms, reusable Action assets, a
selected reload and timing markers. Mesh weights stay on the export skeleton.
Textures and a complete reference GLB live alongside it. Existing workspaces
are kept; `--replace` explicitly rebuilds them and discards edits. These weapon
controls are FK; they do not currently have the body rigs' IK switches.

Use `export_character.py --clip NAME` to bake edited control motion to a staged
single-clip GLB. Do not install that preview over a complete gameplay model:
the remaining clips and source node mapping must be retained when integrating
an edited clip. The deterministic content pass is the installed reload path.

The first local install retains original first-person GLBs under
`local/weapons/before-reloads/`. Generated retail-derived models and authoring
files stay outside source control. Rebuilding content reapplies the motion pass;
do not apply the pass twice to the same GLB.

## Weapon audio

`python content/assets/weapons/author_audio.py` creates the 18-recording bank:
eight firing sounds, eight reload handling sounds, a shell insertion and a pump.
The audio content step installs `weapons.json` after the original/sidearm banks.
Shotgun shell sounds follow actual insertion events, including early exits.
The shell and primary reload hooks retain gameplay timing and ammo accounting.

Recordings, edits and licenses are documented in
`content/assets/weapons/CREDITS.txt`; the export includes the sidearm credits
for its source layers. Original source recordings and deterministic authoring
code are retained. Blender/Mixamo do not provide an audio library: the additional
recordings came from the credited OpenGameArt sources.

