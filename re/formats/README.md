# Package format readers

Python 3.10 readers for package data that `scctpkg` does not cover. Each module has a command
line and writes results under ignored `local/formats/`. The game install is read-only.

The readers use the vendored `tools/scctpkg`, or a Bridge checkout when that copy is absent.
See the [RE setup guide](../README.md) for prerequisites and the
[content pipeline](../../content/README.md) for how the converted data is used.

| Module | Reads | Writes | Spike |
|---|---|---|---|
| `scctformats.ge` | the GE list (ledges, pipes, zip lines, ladders, fences, poles, hand-over-hand bars) in each map's Level export | `local/formats/ge/<Map>.json`, `_summary.json` | [C3-ge-list](../../docs/spikes/C3-ge-list.md) |
| `scctformats.smcollision` | the collision data after a static mesh's render buffers: per-polygon triangles and BSP, hull vertices, planes, triangles and BSP, source triangles | `local/formats/smcollision/<Package>.json`, `_summary.json` | [C2-staticmesh-collision](../../docs/spikes/C2-staticmesh-collision.md) |
| `scctformats.classdefaults` | exact class default objects of the script packages (`System/_PC_/*.u`), with the bytecode walked token by token | `local/formats/classdefaults.json`, `_effective.json`, `_localized.json`, `_vs_<dump>.json` | [P1-class-defaults](../../docs/spikes/P1-class-defaults.md) |

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
