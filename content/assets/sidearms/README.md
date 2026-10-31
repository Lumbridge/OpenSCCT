# Sidearm art sources

The pistols are adapted from four free CC-BY 4.0 assets by Joche007, IVA, Neerhom_mod and ELIZION. The original
downloads, source URLs and licenses are under `sources/`; `CREDITS.txt` records authorship and our changes. The
suppressor and speedloader are original OpenSCCT parts. Run `author.py` in Blender with `--repo <checkout>` to
regenerate the editable `.blend` scenes, third-person parts, speedloader and rendered icons under `export/`.

The first-person models use DJMaesen's credited, fully finger-rigged 2K arms and authored pistol animations.
`svmbuild.pistol_arms` splits the draw/fire/reload/idle clips, fits the gripping fingers to each handle and retargets the
player's converted merc gadget and melee clips onto that rig. Retail geometry and animation exports stay out of
the source assets. Assembled models live in the content output alongside generated finishes and audio.

The camera framing follows the requested CS2-style lower-right stance. Finger, hand and arm poses come from the
authored rig; they move with the gun rather than being approximated by a wrist overlay. Gameplay traces, recoil
rules and third-person pawn movement remain simulation data.

`tools/ci/asset-provenance.json` records the reviewed source and export files by exact path, byte count and SHA-256.
After changing these assets, review their credits and update the recorded hashes. The game-data guard permits only
those exact art files (including the byte-identical copies shipped with contenttools); unlisted media, original
game packages and executable payloads remain rejected.
