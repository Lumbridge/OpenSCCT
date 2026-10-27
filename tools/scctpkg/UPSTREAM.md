# Vendored scctpkg

Python reader for SCCT Versus packages, copied from the Bridge repository.
Do not edit files under `scctpkg/` here: change them upstream and re-run `sync.py`. A fix that cannot wait
goes in `patches/` (applied by `sync.py`, verified by `sync.py --check`) until upstream has it.

- Source repository: https://github.com/Lumbridge/SCCT_Versus_UE5_Bridge.git
- Source path: `scctpkg/` (local checkout `%USERPROFILE%\source\repos\SCCT_Versus_UE5_Bridge`)
- Commit: `839b70270a16ed353a105ca036b6b0163b70672a`
- Commit date: 2026-09-29T00:01:36+01:00
- Vendored on: 2026-10-23

Files are taken from the git object store at that commit, so each one matches its upstream blob id.
`class_defaults.json` is game-derived: it is copied for imports to work but is not tracked
(`tools/scctpkg/.gitignore`). Re-run `python tools/scctpkg/sync.py` on a fresh clone to get it.

A later split of scctpkg into its own repository (consumed here as a git subtree) is planned.

## Files

| File | Blob | Tracked |
|---|---|---|
| `__init__.py` | `4270f62e248dc42cee4aa7b81bf335e7be5b8d66` | yes |
| `__main__.py` | `7837e80526ab975514602fbdfd197191398f4eab` | yes |
| `array_inner_types.json` | `0ace4a4f8a67a10b5946234195ab6ac63015654c` | yes |
| `bsp.py` | `ac7109cf4a4f0bf3bbeead504208053ab2f5dbb2` | yes |
| `bsplight.py` | `37383c1efcdc70233c292b5c14288ce05e0418ce` | yes |
| `class_defaults.json` | `318b719cf94e33693deb434431be85212c5f55de` | no |
| `export_gltf.py` | `d371eb56b5a4c3ad3f115988cf9054c2dd921289` | yes |
| `export_skel_gltf.py` | `64070b09c77d8aebceccf1ab0417513cf7050258` | yes |
| `level.py` | `ca11d02bc34f84b8bc5f2b90dd7224eb5678c142` | yes |
| `materials.py` | `5e0ef50ce1e880ad0588e1e8f2cd93ee80c9ae2c` | yes |
| `meshanim.py` | `a084cf802e17ce21904729604dc9dc9aafd7aa29` | yes |
| `package.py` | `0b6f8881d82a25fd0671aaf70730ec6feb93eae9` | yes |
| `packageset.py` | `f1146d5af7f8d1108e8569ca8406eb0134edc18f` | yes |
| `properties.py` | `8a5a6f6390bdec1845e69a2c884097e8353b3a59` | yes |
| `skeletalmesh.py` | `d2eb0be4268098cef7a47aad60bf99cab5ada1fc` | yes |
| `staticmesh.py` | `bc5a3b8769be0eefba5683c9f7d42ea6d32e3543` | yes |
| `texture.py` | `3f41753979b7677093297ec2f93cdbbb34af5728` | yes |
