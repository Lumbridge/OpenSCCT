# Content packs (OpenSCCT-Content)

[OpenSCCT-Content](https://github.com/Lumbridge/OpenSCCT-Content) holds game content already converted to
OpenSCCT's own formats, so a player (or a server) can download a map and play it without running the converter.
It replaces [SCCT-Maps](https://github.com/Lumbridge/SCCT-Maps) for OpenSCCT only. SCCT-Maps stays as it is for the
original game and SCCT Map Manager.

The repository's tree holds only the index and the tools that make it. The packs themselves are GitHub Release
assets, so git history stays small and downloads have no LFS quota.

## The index

`index.json` on the `main` branch, fetched raw:
`https://raw.githubusercontent.com/Lumbridge/OpenSCCT-Content/main/index.json`. It is the only file a client reads
from the tree.

```json
{
  "format": "openscct.content",
  "formatVersion": 1,
  "updated": "2026-10-28T20:00:00Z",
  "svmapFormat": "1.1",
  "shared": [
    {
      "id": "core",
      "title": "Shared game data",
      "version": "0.8.25.1",
      "contents": ["classes.json", "render", "fx", "ui", "maps"],
      "hashes": { "classes": "<sha256>" },
      "download": { "url": "https://github.com/Lumbridge/OpenSCCT-Content/releases/download/<tag>/shared-core-0.8.25.1.zip",
                    "size": 31457280, "sha256": "<sha256 of the zip>" }
    }
  ],
  "maps": [
    {
      "id": "stock/AquaE",
      "code": "AquaE",
      "title": "Enhanced Aquarius",
      "category": "stock",
      "version": "1.0.0",
      "modes": ["Story", "DiskHunt", "Deathmatch", "MercDeathmatch"],
      "contentHash": "66b2f44d79c6cf46",
      "manifestSha256": "<sha256 of the map's manifest.json>",
      "requires": [ { "pack": "audio", "sha256": "<sha256>" }, { "pack": "classes", "sha256": "<sha256>" } ],
      "source": { "repo": "Lumbridge/SCCT-Maps", "path": "stock/Packages/Maps/AquaE.sdc", "sha256": "<sha256>" },
      "full": { "url": "...", "size": 12345678, "sha256": "<sha256>" },
      "sim":  { "url": "...", "size": 1234567,  "sha256": "<sha256>" }
    }
  ]
}
```

- `formatVersion` changes only when an old client could not read the index. A client skips an index with a newer
  major version and says the game needs an update.
- `svmapFormat` is the `major.minor` of the svmap packs (spec/svmap). A client that reads a different major skips
  every map and says so.
- `shared` packs install to `shared/<contents>` and are what a converted map needs from outside its own folder:
  - `core`: classes.json, the render pack, effects, UI and the map catalog with its images
  - `chars`: characters
  - `audio`: the shared sound banks

  `hashes` are the values the maps' `requires` name (each manifest's `requires[].sha256`).
- `maps`:
  - `id` is `<category>/<code>`, where category is one of: `stock`, `original`, `community`, `enhanced`, `recovered`, `ports`.
  - `code` is the map's package name. It is also the svmap folder name and the name the game's catalog uses.
  - `contentHash` is `MapCatalog.ContentHashOf`: the first 16 hex characters of the manifest's sim hash. This is what lobbies and the hub compare, so a client can tell whether its copy matches without downloading.
  - `full` is everything the game needs to play the map. `sim` is `manifest.json` and `sim/` only, which is what a dedicated server or hub needs.
  - `version` is the pack's version: SemVer, bumped for every republish of the same code.
- The published index also carries `treeSha256` (spots unchanged packs), `generator` (the svmbuild that made the pack)
  and `source.kind` (`install`, `repo` or `release`).
- **Duplicate codes:** a code may appear in two categories (`community/EDE64` and `recovered/EDE64`). A client takes
  the entry whose `contentHash` matches, else the first of stock, original, enhanced, recovered, ports, community
  (`ContentIndex.FindMap`), so a player and a hub agree. Maps without `modes` (Entry, Lobby, the Double Agent ports
  with no player starts) are not offered for play.
- Unknown fields are ignored. Missing optional fields: `title` falls back to `code`, `modes` to empty, `sim` to `full`.

## Pack archives

Every pack is a ZIP (the only archive both .NET and Python read without extra libraries).

- Map archives hold the map folder's contents at the root: `manifest.json`, `sim/`, `render/`, `audio/`, `ui/`,
  plus `catalog/` (see below). Shared archives hold their `contents` at the root.
- Files that are already compressed (`.png`, `.ogg`, `.jpg`) are stored. Everything else is deflated.
- Paths use `/`, are relative, and never contain `..`, a drive letter or a leading `/`. A client refuses any archive
  that breaks this.
- After extracting a map, the client checks each extracted file against `manifest.json`'s `files` (sha256 and size).
  A `sim` archive holds only part of the files; the rest are allowed to be missing.

### A map's catalog entry

The game's map catalog (`shared/maps/catalog.json`, docs/MAPS.md) names maps and holds their menu, briefing and
loading images. A downloaded map brings its own entry, so it shows correctly over a catalog built from the player's
install:

```
catalog/entry.json     { "code": "AquaE", "name": "Enhanced Aquarius", "images": { "menu": "images/menu.png", ... } }
catalog/images/*.png
```

`MapCatalogData` reads these from every map folder to fill missing names and individual images. Existing shared
image files take precedence; a missing image kind or a reference to a missing file falls back to the map's entry.

## Where packs install

| Who | Root | Layout |
| --- | --- | --- |
| Game (launcher install) | `%LOCALAPPDATA%\OpenSCCT\packs` | `svmap/<code>/`, `shared/`, `installed.json`, `downloads/` |
| Dedicated server / hub | `<server>/packs` (or `--content-packs <dir>`) | the same |

`installed.json` lists each installed pack: id, code, version, archive sha256, kind (`full` or `sim`) and when it was
installed.

The game searches its maps in this order:
1. content converted from the player's install (`<content>/svmap`)
2. downloaded packs (`<packs>/svmap`)
3. the maps shipped with the build (`<exe>/maps`)

A converted map of the same code wins, so a player's own conversion is never overwritten. Shared content works the
same way: `<content>/shared` when the player converted their install, else `<packs>/shared`.

## Publishing

The tools live in the OpenSCCT-Content repository (`tools/`); its `docs/PUBLISHING.md` has the steps. In short:
1. Build the content with svmbuild from a full install plus the SCCT-Maps downloads.
2. `pack` writes one archive per map (full and sim) and per shared pack, skipping any whose content is unchanged since the index.
3. `publish` creates a release `content-<yyyy.MM.dd>[-n]` holding only the new archives, then rewrites `index.json` to point at them and commits it.

Older releases stay, so a host and its players can use the same pack until the index moves on.

Releases that add models, icons, finishes or sounds must also refresh the affected shared packs. Shipping the new
content builder with the client only updates players who convert an install; players using downloaded content keep
the assets named by this index. Check the shared archives for the new assets before calling the deploy complete.
