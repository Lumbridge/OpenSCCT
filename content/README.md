# Content pipeline

`svmbuild` converts an installed copy of SCCT Versus into the maps, characters, sound and UI
used by OpenSCCT. It reads the install and writes to `.content/`, which is ignored by Git.
Keep all converted game data out of commits; see the [provenance rules](../re/PROVENANCE.md).

Players can use the [launcher](../docs/LAUNCHER.md). This guide is for working on the converter.
For the output formats, see [SVMap 1.1](../spec/svmap/1.1/README.md) and
[content packs](../docs/CONTENT-PACKS.md).

## Running

Install Python 3.10 and the dependencies, then run from the `content/` directory:

```powershell
python -m pip install -r content/requirements.txt
cd content
python -m svmbuild --list
python -m svmbuild maps validate --maps SteeE
python -m svmbuild maps validate --maps all --jobs 8
python -m svmbuild chars ui audio audio-map maps-catalog renderpack fx --maps SteeE
```

Use `--game <install>` to select the game folder. The default is `SVM_GAME_ROOT`, then the
reference install. `--out <folder>` overrides `SVM_CONTENT` or the repository's `.content/`.
`--maps A,B` selects maps by name; `--maps all` processes the install's full map set.

Useful checks, also run from `content/`:

```powershell
python -m svmbuild validate --maps SteeE --check-stable
python -m svmbuild.testbox --check
python -m svmbuild.vectors --check
```

`--check-stable` converts again and checks for identical output. `--force` rebuilds a step;
`--no-cache` avoids cached results; `--sim-only` omits map rendering data. Use `--report <file>`
for a JSON report or `--help` for the full options.

Run converter tests from the repository root:

```powershell
python -m pytest tests/content
```

Tests that need a local game install or converted content skip when those inputs are absent.
Synthetic tests run without them.

## Steps

`python -m svmbuild --list` shows the current steps and their dependencies.

| Step | Scope | Output |
|---|---|---|
| `classes` | Shared | Typed class defaults in `shared/classes.json` |
| `chars` | Shared | Characters, weapons, props and animations in `shared/chars/` |
| `ui` | Shared | Pages, fonts, atlases and text in `shared/ui/` |
| `audio` | Shared | Sound library in `shared/audio/` |
| `maps` | Per map | SVMap 1.1 folders in `svmap/<Map>/` |
| `audio-map` | Per map | Sound bank and actor/zone sound references in `svmap/<Map>/audio/` |
| `maps-catalog` | Shared | Map lists, names and pictures in `shared/maps/` |
| `renderpack` | Shared | Vision, lighting and gameplay-object assets in `shared/render/` |
| `fx` | Shared | Gameplay effects in `shared/fx/` |
| `validate` | Per map | Schema, reference, collision and hash checks |
| `maps-preview` | Per map | Preview exports in `preview/<Map>/` |
| `testbox` | Shared | Synthetic test map in `maps/TestBox/` |

`textures` and `meshes` are placeholders for separate shared packs. The map step currently
writes the textures and static-mesh data it needs. The [map conversion study](../docs/spikes/P4-maps.md)
records the original all-map validation.

## Editor copies

The `maps` step converts the map file in the install's `Packages/Maps`. That file can be a cooked copy (what the stock
editor's Save writes to `Packages/Maps` and the game loads) or an editor copy (what it writes to `Packages/MapsEd`:
the CSG brushes, a source FPoly and brush link on every BSP surface, the lightmap records, PS2 lighting, static
lights). Nothing in the file says which. `scctpkg` `Package.layout` parses the smallest Model export in both layouts
and takes the one that ends on the export's last byte (patch 0021); the model, lightmap and surface readers branch on
it. An editor copy converts to what its cooked twin converts to:

- `mapconv/source.py` leaves out of the actor list what the cook leaves out of `Level.Actors` (CSG brushes and static
  lights, `MapSource.left_out_by_cook`, decoded from the stock editor) and stores `StaticMeshInstanceOnXBOX` as
  `StaticMeshInstance`, as the cook does.
- The render still draws the left-out lights the cooked save writes anyway because a stored object refers to them
  (`IgnoredEchelonLights`), as it does for a cooked copy (`MapSource.unsaved_refs`).
- `Entry` and `Lobby` lose their world lightmaps: the cook empties them for those two file names.
- The world BSP is the Level's XBOX model (the PC one), else its PSX2 model: some editor copies hold both.
- An editor copy stores sub-emitters ahead of every Emitter actor, where the exporter (which gives a sub-emitter to
  the nearest preceding Emitter export) skips their textures; `mapconv/render.py` writes those from each Emitter's
  own `Emitters` array (this also adds the textures missing from the cooked OffsD, PanaD, Cistern and ScheU).

On 22 of 23 editor and cooked copies from the same save (SVM 4.0 mapping install: AquaE, ClubE, DeftD, Entry, Lobby,
CineS, ShipD, KrauU, MuseD, AeroD, TME30, Cistern; SVM 3.6: AquaE, ClubE, DeftD, CineS, ShipD, StatD, SancD, OffsE,
OffsD, TME30) the two SVMap folders hold the same files with the same content; the files only differ in the order of
entries that follows the package's export order, in glTF material indices, and in `src.export` (the actor's export
index in the file it came from). The exception is the SVM 3.6 `Lobby`, whose cooked copy kept the world lightmap the
stock cook empties for `Lobby.sdc` (the 3.6 and 4.0 installs ship the same editor executable). The editor's
unpatched autosaves (`Auto0..9.sdc`, multi-chunk, summary counts 0) convert too.

## Cache

`graph.py` keys every step run by the sha256 of the canonical JSON of its step name, version, code digest (its own
modules plus the vendored readers), options, declared input digests and the output digests of its dependencies.
Inputs a step discovers while running (the packages a map opened, and the ones it looked for and did not find) are
stored with the entry and checked before reuse. A step owns either a folder or a list of files under the content
root. Entries live in `.content/cache/entries/`, output files once each in `.content/cache/objects/`, and a size
and mtime memo of file digests in `.content/cache/filehash.json`. A hit restores missing or damaged outputs from the
objects. With `--jobs`, consecutive per-map steps run map by map in spawned worker processes; each worker hashes
with its own memo and the parent merges what they computed. A unit whose outputs are not under the content root
fails before it runs. The check compares absolute paths, because on Windows resolving a folder that does not exist
yet while another worker creates its parent can return a different form of the same path; outputs that already
exist are resolved as well, so a link that leads out of the content root is refused before it is cleared. Delete
`.content/cache/` to reclaim space.

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
