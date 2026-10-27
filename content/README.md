# svmbuild

The content pipeline. It reads an original install (read-only) and writes SVMap folders and shared packs under
`.content/` (gitignored). Nothing it produces from game files is ever committed.

Python 3.10 with `content/requirements.txt` (numpy, Pillow, jsonschema; pytest for the tests). Game data is read
with the vendored `tools/scctpkg` and the decoders in `re/formats/scctformats`, which the pipeline imports.

## Running

From `content/` (or with `content/` on `PYTHONPATH`):

```
python -m svmbuild --list                                  steps, dependencies, status
python -m svmbuild maps validate --maps SteeE              classes, then SteeE to .content/svmap/SteeE/, validated
python -m svmbuild maps validate --maps all --jobs 8       every map, 8 worker processes
python -m svmbuild validate --maps SteeE --check-stable    also convert again and require byte-identical files
python -m svmbuild chars                                   characters, weapons, map props to .content/shared/chars/
python -m svmbuild maps-preview --maps SteeE               vendored scctpkg export into .content/preview/SteeE/
python -m svmbuild testbox                                 synthetic TestBox into .content/maps/TestBox/
python -m svmbuild.testbox                                 regenerate the tracked fixtures/testbox/
python -m svmbuild.testbox --check                         fail if fixtures/testbox/ is not what the generator writes
python -m svmbuild.vectors --check                         fail if spec/svmap/1.0/vectors/canonical.json is stale
```

Options: `--maps A,B|all` (default all maps of the install, matched ignoring case), `--game PATH` (default
`$SVM_GAME_ROOT`, else the reference install), `--out PATH` (default `$SVM_CONTENT`, else `<repo>/.content`),
`--jobs N` (per-map steps in N worker processes), `--check-stable`, `--sim-only` (maps without `render/`),
`--report FILE` (every unit's status, time and stats as JSON), `--force`, `--no-cache`, `--no-deps`, `--rehash`.

Tests: `python -m pytest tests/content` from the repository root. `test_maps_steee.py` converts SteeE from the
reference install and skips when it is not there; the rest use synthetic data only.

## Steps

| Step | Scope | Status | Output |
|---|---|---|---|
| classes (P1) | global | ready | `shared/classes.json`: exact class defaults of the six script packages, typed |
| textures (P2) | global | stub | (maps write their PNGs into `render/textures/` for now) |
| meshes (P3) | global | stub | (maps decode static mesh collision themselves for now) |
| chars (P5) | global | ready | `shared/chars/<Package>/`: GLB skins, `<Animation>.anim.json`, textures |
| audio (P6) | global | stub | `packs/audio/` |
| ui (P7) | global | ready | `shared/ui/`: Magma pages, atlases, .mft fonts, Magma strings (docs/HUD.md) |
| maps (P4) | per map | ready | `svmap/<Map>/`: SVMap 1.1 (`spec/svmap/1.1`) |
| validate (P8) | per map | ready | none (schemas, references, derived indexes, collision, hashes; `--check-stable`) |
| maps-preview | per map | ready | `preview/<Map>/` |
| testbox | global | ready | `maps/TestBox/` |

Results on the 83 shipped maps: `docs/spikes/P4-maps.md`.

## Cache

`graph.py` keys every step run by the sha256 of the canonical JSON of its step name, version, code digest (its own
modules plus the vendored readers), options, declared input digests and the output digests of its dependencies.
Inputs a step discovers while running (the packages a map opened, and the ones it looked for and did not find) are
stored with the entry and checked before reuse. A step owns either a folder or a list of files under the content
root. Entries live in `.content/cache/entries/`, output files once each in `.content/cache/objects/`, and a size
and mtime memo of file digests in `.content/cache/filehash.json`. A hit restores missing or damaged outputs from the
objects. With `--jobs`, consecutive per-map steps run map by map in spawned worker processes; each worker hashes
with its own memo and the parent merges what they computed. Delete `.content/cache/` to reclaim space.

## Layout

```
svmbuild/
  cli.py          command line
  graph.py        step graph, cache keys, cache store and restore, worker processes
  hashing.py      file digests with a stat memo
  canonical.py    canonical JSON, float32 text form, SVMap group hashes (spec/svmap/1.0/conventions.md)
  svmap.py        derived gameplay indexes (1.0 and 1.1 rules) and the SVMap folder writer
  validation.py   schema (per format minor), reference, collision, class table, render and hash checks
  testbox.py      synthetic TestBox generator
  vectors.py      writes the shared canonical test vectors
  charconv.py     skeletal meshes and animation sets (chars step)
  sources/        readers on top of scctpkg and scctformats: typed props, class defaults, full UModel, animations
  mapconv/        one map to SVMap 1.1: source, gameplay, collision, traversal, render, build
  steps/          one module per step
  writers/        collision.bin and glb writers and readers
```

## Adapters on the shared readers

The pipeline does not change `tools/scctpkg` or `re/formats/scctformats`; where it needs more it reads around them:

- `sources/typedprops.py` subclasses `scctformats.classdefaults.Decoder` so object references stay references (with
  the package that stored them) and names stay names.
- `sources/modeltail.py` reads the UModel fields `scctpkg.bsp.read_model` skips (node collision bound, zones, leaf
  indices) and the zone table after `Verts` (12-byte records, checked on SteeE, OrphD and Entry).
- `sources/animations.py` reads the MeshAnimation notifies that `scctpkg.meshanim` discards: a notify record is
  `f32 time, name Function, objref Notify`; scctpkg reads the name as a u16, which only works while both of its
  compact indices are below 64.
- `mapconv/render.py` clears `scctpkg.export_gltf._EMITTER_PNGS` before each export: the module-level memo would
  skip emitter PNGs when a process exports twice into a cleared folder.
