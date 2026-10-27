# re/formats: decoders for package data scctpkg does not cover

Python 3.10 modules, each with a command line, that read the reference install (read-only) and
write their results under `local/formats/` (gitignored). They import `scctpkg` from
`tools/scctpkg` (or a Bridge checkout when that copy is missing).

| Module | Reads | Writes | Spike |
|---|---|---|---|
| `scctformats.ge` | the GE list (ledges, pipes, zip lines, ladders, fences, poles, hand-over-hand bars) in each map's Level export | `local/formats/ge/<Map>.json`, `_summary.json` | `docs/spikes/C3-ge-list.md` |
| `scctformats.smcollision` | the collision data after a static mesh's render buffers: per-polygon triangles and BSP, hull vertices, planes, triangles and BSP, source triangles | `local/formats/smcollision/<Package>.json`, `_summary.json` | `docs/spikes/C2-staticmesh-collision.md` |
| `scctformats.classdefaults` | exact class default objects of the script packages (`System/_PC_/*.u`), with the bytecode walked token by token | `local/formats/classdefaults.json`, `_effective.json`, `_localized.json`, `_vs_<dump>.json` | `docs/spikes/P1-class-defaults.md` |

Run from `re/formats` (or put it on `PYTHONPATH`):

```powershell
cd re/formats
python -B -m scctformats.ge all --census --compare MapsEd
python -B -m scctformats.smcollision all
python -B -m scctformats.classdefaults --live ../../local/reflect/game_cdo.json
python -B -m pytest            # synthetic data only, no game files needed
```

Environment overrides: `SVM_GAME_DIR` (reference install), `SVM_LOCAL` (output root),
`SVM_BRIDGE_ROOT` (fallback scctpkg).

Rules for this folder: tests build their input bytes themselves (`scctformats._writer.Writer`);
nothing derived from the game (bytes, dumps, decoded values) is committed here. Numbers measured on
the real files go in the spike documents.
