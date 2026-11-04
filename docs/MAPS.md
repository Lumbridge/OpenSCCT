# Map catalog and validation

Use the map catalog to find installed maps, supported modes and playlists. The tools
below validate content and run bot matches across maps.

The sweep tables record specific runs from October 2026, not the current health of
every map. The [1 November map parity report](MAP-PARITY-2026-11-01.md) records a later
check of actors, events, interactions, elevators and destructible effects, with its
coverage limits.

**On this page:** [The map catalog (API for the menus)](#the-map-catalog-api-for-the-menus) · [Map identity](#map-identity) · [Map pictures](#map-pictures) · [The tools](#the-tools) · [Core findings](#core-findings)

## The map catalog (API for the menus)

`OpenScct.Server.MapCatalog` (src/OpenScct.Server/Maps) is what the host screen, the lobby and the launchers ask
for maps. The calls below are stable; new fields may be added to the records, none will be renamed or removed.

```csharp
MapCatalog maps = MapCatalog.CreateDefault();            // or service.Catalog.Maps in the game
IReadOnlyList<MapCatalogEntry> host = maps.GetHostMaps(); // the host screen's list, Reloaded order
IReadOnlyList<MapCatalogEntry> all = maps.GetCatalog();   // every installed map, menu and training maps too
MapCatalogEntry? e = maps.GetCatalogEntry("AquaE");
IReadOnlyList<MapPlaylist> lists = maps.GetPlaylists();   // Reloaded's playlists, then "All"
IReadOnlyList<MapPlaylist> filters = maps.GetMapFilters(); // All, Enhanced, Stock, Community, 3v3 optimised
```

`MapCatalogEntry` (one map):

| Field | What | Source |
|---|---|---|
| `Id` | the map code and file name (`AquaE`); what `PlaySettings.Map`, `LobbySetup` and `!map` take | folder name |
| `DisplayName` | the name every page shows, upper case as the original's menus (`ENHANCED AQUARIUS`) | the map's identity name upper-cased, else `SMapSettings.MapName` of `<Map>-i.utc` |
| `Name` | the name Reloaded's lobby shows (`Enhanced Aquarius`) | the map's identity, else Reloaded's map name table, else the manifest title |
| `SortName`, `VariantWord` | the name without its variant word (`Aquarius`, `Enhanced`), as Reloaded sorts and dims it | the identity's family and variant, else Reloaded's variant words |
| `Kind` | `Versus` (hostable), `Menu` (Entry, Lobby), `Training` (TSP, TME), `Test` (our own TestRange, fixtures) | the original's map list rules; outside the original's list, maps converted from a real map and editor maps (`source.kind` `authored`) with a playable mode are `Versus` |
| `HostOrder`, `OriginalOrder` | index in `GetHostMaps()` (Reloaded order) and in the original's host list (-1 when not listed) | see "Order" |
| `Modes` | one `MapModeInfo` per mode the original's mode box offers (Story, Disk Hunt, Deathmatch, Merc Deathmatch) | see "Modes" |
| `DefaultMode` | the mode to preselect: Story when it plays, else the first that plays | |
| `SpyStarts`, `MercStarts`, `MercDeathmatchStarts` | player starts per side, and the Merc Deathmatch set | the map's `PlayerStart`s |
| `RecommendedPlayers` | the map's own choice; else 6 for the maps of Reloaded's 3v3 playlist (its playlist of maps made for more players) or tagged `3v3`; else 4 (the original's Max Players default) | identity `players.recommended` |
| `Description`, `Authors`, `Version`, `Tags`, `Places` | what an editor map says about itself (empty for converted maps) | `ui/identity.json` |
| `Playlists` | the Reloaded playlists the map is in | |
| `PreviewImage` | the map list picture (PNG or JPEG path, null when the map has none) | the map's `ui/pictures`, else `<Map>-i.Menu` as `LevelInfo.GetLevelPicture` loads it |
| `LoadingSpyImages`, `LoadingMercImages` (`LoadingSpyImage`, `LoadingMercImage` the first of each) | the loading screens of each side; a map may carry several | the map's `ui/pictures`, else `<Map>-i.LoadingATT0`, `LoadingDEF0` |
| `BriefingImage`, `BriefingThroughSnapshot` | the lobby briefing's plan of the map, and whether it was drawn through the snapshot camera (then the lobby draws the place numbers on it) | the map's `ui/pictures`, else `<Map>-i.Briefing` |
| `Legend` | the map text under the picture (`LegendMap`, lines split by `|`) | `SMapSettings` |
| `Folder`, `ContentHash`, `CanRender` | the SVMap folder, its content hash, whether it has the render content the play scene draws | manifest |

`MapModeInfo`: `Mode` (`EGameMode`), `Name` (Story, Disk Hunt, Deathmatch, Merc Deathmatch), `Playable` (the mode
has what it plays with on this map, below), `HasSettings` (the map has an `SMapSettings` entry for the mode; the
host menu starts from it, else from the menu defaults), `Objectives` and `ObjectivesNeeded` (the mode's mission:
Story objectives and the minimum to take, Disk Hunt disks placed and the disks to take), `Defaults` (the entry:
time limit, lives, respawn, disks; null without one).

`MapPlaylist`: `Name`, `Description`, `Mode` (the mode the playlist is for), `Maps` (installed map codes, in host
order), `Missing` (codes the playlist names that are not installed, such as `StatP`).

The map browser uses `GetMapFilters()` independently of Reloaded's optional playlists. It always offers
**All installed maps**, **Enhanced maps**, **Stock maps**, **Community maps** and **3v3 optimised maps**.
Stock is the eleven launch maps: `AquaD`, `BankD`, `ClubD`, `DeftD`, `FactD`, `MissD`, `MuseD`, `OrphD`,
`RiveD`, `StatD`, `WareD`. Later downloads such as Polar Base and Steel Squat are outside that group.
The launch roster is recorded in [Official Xbox Magazine, March 2005, p. 52](https://datassette.s3.us-west-004.backblazeb2.com/revistas/official_us_xbox_magazine_web_version_2005-03.pdf).
Enhanced uses the `Enhanced` or `Enh` prefix in the catalog or original menu name and excludes 3v3 variants from its
filter. Community contains everything outside Stock and the Enhanced family, including community 3v3 maps;
enhanced 3v3 maps remain in All and 3v3 optimised. The 3v3 group uses Reloaded's `3v3` playlist, an explicit `3v3`
label in the map's name or code, or a map's own identity (a `3v3` tag, or exactly six recommended players). These
groups work without the shared catalog data and keep the host map order. Search (map name, code, authors and tags)
and the selected mode further narrow the visible results.

### Modes

The original's mode box (`moListEditBox_GameMode.FillList`, native 1460, and `RefreshList`) offers the same four
modes on every map: Story, Disk Hunt and Deathmatch from the native, Merc Deathmatch added by the script. The map
only gives the starting values. The catalog keeps all four on every map and says for each whether it plays:

- **Story**: the map has a Story mission (`GameMode` 5) with objectives.
- **Disk Hunt**: the map has a Disk Hunt mission (`GameMode` 4) with disks.
- **Deathmatch**: always (kills end it); every map has spy and merc starts.
- **Merc Deathmatch**: always; maps with `bMercDeathMatchStart` starts use that set (Reloaded's Merc Deathmatch
  playlist), the others the normal merc and spy starts (`spawn-selection.md` rule 8).

Extraction, Sabotage and Neutralization (`EGameMode` 1 to 3) are not in the original's mode box and no stock map
has a mission or a settings entry for them ([spec/game/game-modes.md](../spec/game/game-modes.md) rule 16). The catalog lists them
(`MapCatalog.ExtraModes`, after the four menu modes) only for a map that has a mission or an `SMapSettings.Modes`
entry of the mode, playable when that mission has objectives and the map has spy and merc starts; the Create
Session page offers them from the map's settings entries (an OpenSCCT choice, for custom maps). The Sabotage (S)
and Extraction (X) map variants are Story maps whose objectives are bombs or disks.

### Order

- **Original** (`OriginalOrder`): `GUIController.GetMapList` (native 1632, the list builder at game 0x109e31b0)
  lists the map files of the map folder in file order, skips `Entry` and `Lobby` and every `TME**` and `TSP**`
  map, so tutorials and menu maps never show in the host list. The catalog sorts the codes as the file listing
  does (case-insensitive).
- **Reloaded** (`HostOrder`, the default): the lobby's map list sorts by name with the variant word skipped, so
  every version of a map sits with the original (Reloaded's config text for `map_variant_words`); ties keep the
  map code order (see the open questions).
- **Training**: the tutorials menu (`Tuto_MapsList`) plays the spy maps `TSP20`, `TSP21`, `TSP22`, `TSP23`,
  `TSP29` (exam last) and the merc maps `TME30`, `TME32`, `TME33`, in that order.

### Where the data comes from

`python -m svmbuild maps-catalog` writes `.content/shared/maps/`: `catalog.json` (the original's map file
order, Reloaded's playlists, map names and variant words, decoded from `Reloaded.Core.dll`'s defaults, and the
images each map has) and `images/<Map>/{menu,loading-spy,loading-merc,briefing}.png` from `<Map>-i.utc`. The
catalog reads it beside the SVMap root (`shared/maps` next to `svmap`) and reads the rest from each map's `ui/mapinfo.json` and `sim/gameplay.json`. Without the folder every
map still lists, with its manifest title as both names, no images and no playlists.

The launcher's **Maps** window downloads ready-to-play [content packs](CONTENT-PACKS.md), whose catalog
and images are included in the shared packs. Developer builds can still convert original-format addons
with `--addons <folder>`; a map absent from Reloaded's name table uses its manifest title.

### Map identity

A map made in the map editor carries its identity in its own folder (SVMap 1.2,
[spec/svmap/1.2/README.md](../spec/svmap/1.2/README.md)): `ui/identity.json` (name, dossier, description, authors,
version, recommended players, tags, numbered places), `ui/pictures/` (menu, loading pictures per side, briefing
plan, banner) and `ui/text/<lang>.json` (its texts in other languages). `MapIdentity` (src/OpenScct.Content/Maps) reads it; `MapFacts.Load` reads it with the rest, so the catalog
needs no other file. The catalog takes each value from the map first, then the shared catalog, then a downloaded
map's `catalog/entry.json`, then `SMapSettings` (the spec's table lists the order per value). Playlists never come
from a map: a map states facts (tags, recommended players) and the filters above pick it up from them.

`MapFacts.Load(folder, language)` and `MapIdentity.Load(folder, language)` overlay the map's `ui/text/<lang>.json`
(`MapText`) for the content language (`ContentPaths.Language`, from `content-build.json`); a missing text keeps the
map's own. The catalog's `StatedPlayers` is the map's own `players.recommended` only: when the host changes the map in
Create Session or the lobby, the player slots follow it unless the host set them by hand. The picture grid's landscape
cards and the thumbnail list show the map's banner (`BannerImage`), else its menu picture.

The lobby's Briefing shows the map's menu picture on page 1 (the original's level picture, `GetLevelPicture`) and
its briefing plan on page 2 (`GetLevelBrief`, the mercs' loading picture when there is none). A plan drawn through
the snapshot camera gets the map's place numbers drawn on it, and the strategic map draws them on each floor.

### Map pictures

`--svm-mapshot=<request.json>` renders map pictures with the game's own renderer and quits: the map editor uses it
for menu and loading pictures taken from a camera bookmark. The request is the golden camera format
(`svm.golden-cameras/1`, `GoldenShots`) without pawns:

```json
{"format": "svm.golden-cameras/1", "map": "MoonD", "folder": "C:/maps/MoonD", "out": "C:/shots", "mode": "Story",
 "shots": {"menu": {"width": 1024, "height": 1024, "look": "plain",
                    "camera": {"location": [0, 0, 512], "rotation": [-4096, 8192, 0], "fov": 90}}}}
```

`folder` is optional (the map is otherwise found by id like any other); `out` defaults to the request's folder;
`look` is `plain`, `nightvision` or `thermal` (RendMap 5, 34 and 36). Each shot is drawn in an `OffscreenView` at
twice its size (as far as 4096 pixels allow) and scaled down to it, so edges are smooth where golden renders keep
hard ones, and saved as `<id>.png` (or its `image`). The run uses default settings, writes none of the player's and
starts no online presence. It prints one `SVM_MAPSHOT` JSON line per shot and exits 0 when every shot was saved. It
needs a real renderer: it always runs as a background window and refuses `--headless` (with an `SVM_MAPSHOT` error
line, which tells a tool the game knows the option). Godot shows the window before any game code runs, so a caller
that must not take the person's focus starts it off-screen (`--position -32000,-32000`) or, for a local test build,
beside `game/override.cfg` (local only, docs/TESTING-PLAY.md).

## The tools

| Tool | What |
|---|---|
| [tools/mapsweep](../tools/mapsweep) (`OpenScct.MapSweep`) | the sweep: `dotnet run --project tools/mapsweep -c Release -- --maps=all --modes=all --seconds=240 --parallel=6` |
| `--maps=` | `all` (every installed map of the catalog: host, training, menu and our own maps), `host` (the host list), `enhanced` (the Enhanced versions, the E maps), `default` (the maps with no variant word, such as the D maps), or a comma list |
| `--modes=` | `all`: one run per mode that plays on the map (the catalog's `Playable`; a front end map is loaded once as a server and as a client, not played), or a comma list (`Story,DiskHunt,Deathmatch,MercDeathMatch`); reports are then `<map>-<mode>.txt` |
| `--side=merc`, `--bots=N`, `--mode=`, `--seed=`, `--out=` | the local player's side, the server bots (3: a 2v2), the mode without `--modes` (the one the host menu preselects: Story when it plays, else the first mode that plays), the seed, the report folder (default `local/test-results/mapsweep`); a tutorial is always played alone as its side (TSP spy, TME merc) |
| `--catalog` | prints the map catalog (docs/MAPS.md "The map catalog") as a table, then the playlists |
| `--probe=Map:x,y,z[:merc[:cells[:colliders]]]` | draws the bots' walk graph around a point, the floors and fit tests of the columns there, the colliders near it |
| `--place=Map:x,y,z:yaw:keys:seconds[:merc]` | puts the local player at a point as a respawn does (server move and client reinit) and holds keys (`BT_Up+bCrouch`), printing the client and server state four times a second |
| `--svm-smoke=map-shots` | the game ([game/Scripts/Smoke/MapShotsSmoke.cs](../game/Scripts/Smoke/MapShotsSmoke.cs)): each map as the launcher plays it (spy, Play Level rules, host thread, HUD), four quarter turns of the view with the mouse, then the goggle key (night vision) and four more; with a window and `SVM_PLAY_SHOTS` it saves `<map>-<n>.png` and `<map>-nv<n>.png`; `SVM_MAPSHOTS_MAPS` limits the maps (default: every host and training map; a TME tutorial as a merc) |

A sweep of one map is the offline play session (`PlaySession`, inline host, loopback) with three server bots next to
a local spy whose keys walk forward and turn away from walls (the input bytes the bound keys give, at the speed the
spy spawns with, which is the slow one; held still on a pipe, ladder or bar he lets go with the crouch key, as the
original's `s_OnPipe` takes it), stepped as fast as it runs for 240 s of simulated time. It reports:

- **load**: the map's actors created as markers (no C# class), spawns that failed, references to actors that did
  not spawn, the props that could not be applied (`UnappliedDetails` in the JSON);
- **spawn**: player starts where a pawn spawned as `RestartPlayer` places it is inside world geometry, or has no
  floor within 400 uu;
- **nav**: starts that are on no node of the bots' walk graph or whose node reaches only a small island of it, and
  bots that never got 600 uu from their spawn;
- **objective**: objective triggers of the swept mode that no spy start reaches on the spy walk graph;
- **world, fall, embedded, stuck**: every half second, each pawn on the server below the kill height or in the
  out-of-world fall, falling for 6 s while alive, inside world geometry for a second, or moving less than 64 uu in
  45 s while alive;
- **exception**, **frame** (frames of server and client over 16.7 ms), **net** (reinits of the local pawn).

Reports: `<map>.txt`, `<map>.json` and `summary.md` in the output folder (under `local/`, never tracked).

## Results: every map in every mode (sweep of 2026-10-25, 240 s per run, seed 1)

`--maps=all --modes=all`: the 85 installed maps (73 host maps, 8 tutorials, Entry and Lobby, TestRange and the
TestBox fixture) in each mode that plays on them, 305 runs. Every map loads (Entry and Lobby as a server and as a
client, with their physics); every played run started its match with the local player through the bound keys and
the loopback link; no run raised an exception, no pawn fell out of the world or through a floor. The 41 maps not
drawn before (the P, S, T, U and X variants and the tutorials, a TME tutorial as a merc) were drawn with a window
(`map-shots`, 328 frames): all drew, no magenta, no missing texture or mesh; materials not drawn are listed below
the older tables. A cell is "plays" with the Story or Disk Hunt objectives taken / needed, and in brackets the
core areas with findings (spawn, embedded, net); the last column the bot findings (walk graph islands, objectives
out of the bots' reach, stuck bots, failed traversal links), which belong to the bots ([src/OpenScct.Game/AI](../src/OpenScct.Game/AI)):
bots have no Disk Hunt, Deathmatch or Merc Deathmatch goals yet (Merc Deathmatch: no kills in 75 runs).

| Map | Story | Disk Hunt | Deathmatch | Merc Deathmatch | Bot gaps (AI) |
|---|---|---|---|---|---|
| AquaD | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| AquaE | plays 1/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| AquaT | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| BankD | plays 1/2 | plays 0/10 | plays | plays | nav, objective, stuck |
| BankP | plays 1/2 | plays 0/10 | plays | plays | nav, objective, stuck |
| CineE | plays 1/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| CineS | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| CineU | plays 2/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| ClarD | plays 1/6 | plays 0/48 | plays | plays | nav, objective, stuck |
| ClubD | plays 0/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| ClubE | plays 0/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| ClubS | plays 0/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| ClubT | plays 0/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| CpusS | plays 0/5 | - | plays | plays | nav, objective, stuck |
| DeftD | plays 0/2 | plays 0/12 | plays | plays | nav, objective, stuck |
| DeftS | plays 0/2 | plays 0/12 | plays | plays | nav, objective, stuck |
| Entry | loads (front end map) | | | | |
| FactD | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| FactE | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| FactP | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| FactT | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| GhosS | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| KrauE | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| KrauU | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| Lobby | loads (front end map) | | | | |
| MissD | plays 2/3 | plays 0/15 | plays | plays | nav, objective, stuck |
| MissE | plays 2/3 | plays 0/15 | plays | plays | nav, objective, stuck |
| MissP | plays 2/3 | plays 0/15 | plays | plays | nav, objective, stuck |
| MounE | plays 0/3 (net) | plays 0/20 | plays | plays | nav, objective, stuck, traversal |
| MounS | plays 0/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| MounU | plays 0/3 (net) | plays 0/20 | plays | plays | nav, objective, stuck, traversal |
| MuseD | plays 1/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| MuseE | plays 1/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| MuseT | plays 1/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| OffsD | plays 0/4 | plays 0/15 | plays | plays | nav, objective, stuck |
| OrphD | plays 2/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| OrphE | plays 2/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| OrphS | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| OrphT | plays 2/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| PanaD | plays 1/2 (embedded, spawn) | plays 0/20 (spawn) | plays (spawn) | plays (spawn) | nav, stuck |
| PanaS | plays 1/2 (embedded, spawn) | plays 0/10 (spawn) | plays (spawn) | plays (spawn) | nav, stuck |
| PolaD | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| PolaE | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| PolaP | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| PolaT | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| PrisD | plays 0/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| PyroD | plays 1/3 | plays 0/32 | plays | plays | nav, objective, stuck |
| RiveD | plays 1/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| RiveE | plays 1/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| RiveP | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| SancD | plays 0/3 | plays 0/28 | plays | plays | nav, objective, stuck, traversal |
| SancE | plays 0/3 | plays 0/28 | plays | plays | nav, objective, stuck |
| ScheE | plays 2/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| ScheU | plays 0/3 | plays 0/18 | plays | plays | nav, objective, stuck |
| ShadD | plays 0/3 | - | plays | plays | nav, objective, stuck |
| ShadP | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck, traversal |
| SkysD | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| SkysP | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| SlauS | plays 5/3 | - | plays | plays | nav, stuck |
| StatD | plays 1/2 | plays 0/8 | plays | plays | nav, objective, stuck |
| StatE | plays 1/2 | plays 0/8 | plays | plays | nav, objective, stuck |
| StatS | plays 1/2 | plays 0/8 | plays | plays | nav, objective, stuck |
| SteeD | plays 2/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| SteeE | plays 3/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| SteeP | plays 2/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| SteeT | plays 7/4 | plays 0/20 | plays | plays | nav, objective, stuck |
| StroD | plays 0/4 | plays 6/20 | plays | plays | nav, objective, stuck |
| TermD | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| testbox | plays 0/2 | - | plays | plays | nav, objective, stuck |
| TestRange | plays 0/2 | - | plays | plays | nav, stuck |
| TME30 | plays 0/0 | - | - | - | none |
| TME32 | plays 0/3 | - | - | - | none |
| TME33 | plays 0/0 | - | - | - | none |
| TSP20 | plays 0/0 | - | - | - | none |
| TSP21 | plays 0/1 | - | - | - | none |
| TSP22 | plays 0/3 | - | - | - | none |
| TSP23 | plays 1/2 | - | - | - | none |
| TSP29 | plays 2/3 | - | - | - | none |
| VertE | plays 1/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| VertU | plays 0/3 | plays 0/30 | plays | plays | nav, objective, stuck |
| VertX | plays 0/2 | plays 0/20 | plays | plays | nav, objective, stuck |
| WareD | plays 2/3 | plays 0/10 | plays | plays | nav, objective, stuck |
| WareP | plays 0/3 | plays 0/10 | plays | plays | nav, objective, stuck |
| ZiopD | plays 0/3 | plays 0/20 | plays | plays | nav, objective, stuck |
| ZiopS | plays 0/2 | plays 0/20 | plays | plays | nav, objective, stuck |

Findings of this sweep beyond the bots:

- MounE, MounU (M or net): the local spy walks into a corner at (4032, -902, -615) and his pawn is reinitialised
  twice on the client around it; the same in every mode.
- PanaD, PanaS (M): the two skylight spy starts hang 500 uu over the floor (listed below); a spy bot was seen inside
  the level geometry at (6077, -759, 53) in Story.
- TestRange and the TestBox fixture (ours) end Deathmatch with a spy win: their Deathmatch missions have objectives
  (`game-modes.md` edge case); not a stock map.

## Results: Story and Deathmatch maps (earlier sweep of 2026-10-25, 240 s per map, seed 1; shots of the same day)

Every one of the 40 maps loads, every placed actor has a C# class (no markers, no failed spawns; the props left
unapplied are render-only references such as `StaticMeshInstance` and `ZoneEffect`), the match starts on every map,
no exception was raised, no pawn fell out of the world or through a floor, no pawn ended inside geometry, and the
local spy walked between 320 and 8300 uu from his start on every map. All 40 maps draw: no missing texture or mesh,
no magenta, a few materials not drawn (listed below). Columns: **Load** (markers / failed spawns), **Sim** (round
state after 240 s, objectives taken / needed), **Bots** (farthest each bot got from its spawn: spy bot, merc bots),
**Visual** (the shots), **Issues** with C for core and M for map-specific.

### Enhanced maps (E), played in Story

| Map | Status | Load | Sim | Bots (uu) | Visual | Issues |
|---|---|---|---|---|---|---|
| AquaE | ok, bot gaps | 0 / 0 | running, 0/3 | 2989; 2559, 1247 | ok | C: spy starts on a walk graph island, 3 bomb spots out of the spy graph's reach |
| CineE | ok | 0 / 0 | running, 1/3 | 5292; 4194, 3276 | ok, dark at spawn | C: 1 objective out of the spy graph's reach |
| ClubE | bot gaps | 0 / 0 | running, 0/4 | 1609; 467, 2119 | ok, warm orange light on the roof | C: the slow spy could not climb the ladder (fixed); spy starts on the roof reach 2% of the spy graph and no terminal (ladders have no walk graph links) |
| FactE | bot gaps, slow frames | 0 / 0 | running, 0/3 | 3373; 4939, 6319 | ok | C: 125 slow frames (bots replanning); spy starts on an island; 5 objectives out of reach |
| KrauE | ok | 0 / 0 | running, 0/3 | 3229; 2118, 1483 | ok | none |
| MissE | mercs stuck | 0 / 0 | running, 2/3 | 993; 0, 0 | ok | M: merc starts on the concrete stairs (EscaBeton) that end at the bunker door static mesh (doorBunkerExit, blocking, as in the map package); the stairs' hull ends in a 300 uu drop at the foot, which a player walks off but the merc walk graph does not link; the starts sit about 140 uu above the hull's surface (open question below) |
| MounE | ok | 0 / 0 | running, 0/3 | 2797; 6371, 2431 | ok | none |
| MuseE | ok | 0 / 0 | running, 0/3 | 1931; 2850, 1356 | ok | C: 2 objectives out of reach |
| OrphE | mercs stuck | 0 / 0 | running, 2/3 | 6014; 0, 0 | ok | C: merc starts on walk graph islands, 8 objectives out of reach |
| PolaE | bots stuck | 0 / 0 | running, 0/3 | 349; 0, 0 | ok | C: spy and merc starts on walk graph islands (a player walks out: 2174 uu as a merc), 10 objectives out of reach |
| RiveE | ok | 0 / 0 | running, 0/3 | 4029; 5673, 3645 | ok | C: a spy start on an island |
| SancE | bot gaps | 0 / 0 | running, 0/3 | 1773; 5871, 2825 | ok | C: 10 objectives out of reach |
| ScheE | ok | 0 / 0 | running, 2/3 | 7620; 4624, 6078 | ok, one material not drawn | none |
| StatE | mercs stuck, slow frames | 0 / 0 | running, 1/2 | 1519; 0, 0 | ok | C: 341 slow frames, mean 7 ms (0.5 ms without bots): failing plans flood the graph; merc starts on islands |
| SteeE | ok | 0 / 0 | running, 2/4 | 2403; 4709, 2714 | ok, dark at spawn (as the original) | C: the Story disk (SFlag) out of the spy graph's reach |
| VertE | ok | 0 / 0 | running, 2/3 | 8149; 2844, 7527 | ok, one material not drawn | none |

### Default maps (D), played in Deathmatch

The last letter of a map code is its version, not its mode: E is the Enhanced version and D the default map, and
both play every mode their actors support (SteeD plays Story as SteeE does). This sweep picked Deathmatch for the D
maps from that letter, which was wrong; the sweep now plays each map in the mode the host menu preselects (Story
when it plays), so the next sweep plays the D maps in Story.

In Deathmatch the bots have no goal ([src/OpenScct.Game/AI](../src/OpenScct.Game/AI) has no Deathmatch rules): mercs stand at their spawn
and spies idle on every D map. That is core (AI) and is not repeated per row; the Bots column shows the spy bot.

| Map | Status | Load | Bots (uu) | Visual | Issues |
|---|---|---|---|---|---|
| AquaD | ok | 0 / 0 | 1665 | ok | C: spy starts on a walk graph island |
| BankD | ok | 0 / 0 | 398 | ok, one material not drawn | C: starts on islands |
| ClarD | ok | 0 / 0 | 1663 | ok, two engine materials not drawn | none |
| ClubD | ok | 0 / 0 | 1583 | ok, warm orange light | C: spy starts on the roof island (ladders) |
| DeftD | ok | 0 / 0 | 2888 | ok, dark at spawn | none |
| FactD | ok | 0 / 0 | 2823 | ok | C: spy starts on an island (mercs move here: the map has guard posts) |
| MissD | ok | 0 / 0 | 1164 | ok | M: the MissE merc stairs (merc starts on an island) |
| MuseD | ok | 0 / 0 | 2915 | ok | none |
| OffsD | ok | 0 / 0 | 603 | ok | M: the sea under the rig is an active SDamageVolume; a pawn that walks off is killed in the air and its body falls 10 s (`PlayerFallingInerte`) before the respawn, as the volume and the state are ported |
| OrphD | ok | 0 / 0 | 9748 | ok | C: merc starts on islands |
| PanaD | ok | 0 / 0 | 1295 | ok | M: two spy starts (PlayerStart1470, 8282) hang 500 uu over the floor under the skylight; the spy drops in (under the 600 uu fall damage height); C: merc starts on an island |
| PolaD | ok | 0 / 0 | 355 | ok | C: spy starts on an island |
| PrisD | ok | 0 / 0 | 1693 | ok, three stair materials not drawn | none |
| PyroD | ok | 0 / 0 | 3319 | ok | none |
| RiveD | ok | 0 / 0 | 785 | ok | C: starts on islands |
| SancD | ok | 0 / 0 | 2685 | ok | none |
| ShadD | ok | 0 / 0 | 2142 | ok, three materials not drawn (water, door lock, stinger) | C: starts on islands |
| SkysD | ok | 0 / 0 | 297 | ok | C: starts on islands |
| StatD | ok | 0 / 0 | 1517 | ok | C: starts on islands |
| SteeD | ok | 0 / 0 | 1380 | ok, dark | none |
| StroD | ok | 0 / 0 | 2570 | ok | the Deathmatch mission of StroD lists 13 objectives (4 needed); all are out of the spy graph's reach |
| TermD | ok | 0 / 0 | 1144 | ok, six materials not drawn | C: starts on islands |
| WareD | ok | 0 / 0 | 1863 | ok | none |
| ZiopD | ok | 0 / 0 | 2528 | ok | none |

Materials not drawn (render warnings, `material not drawn (no texture or flags)`): `Engine.DefaultTexture` (PyroD,
SancD, SteeD, SteeE, ZiopD), `Engine.ConsoleBK` and `Engine.WhiteSquareTexture__unlit` (ClarD),
`BNK_projo_generique` (BankD), `BLK_Det_Escalier01` slots 2 to 4 (PrisD), `a_ConduitAttPlfdB02` (ScheE),
`GAR_water`, `doorlock_green`, `stinger` (ShadD), five `Terminus_STM` meshes (TermD), `WindowLib_Sky` (VertE),
`sqt_grue_coll01` (SteeD, SteeE). None showed as a hole or a wrong colour in the shots.

## Core findings

### Fixed: a slow spy could not climb a ladder (or a pipe)

A spy spawns in the slow speed mode, where a held key gives a stick of 2400. `ControlInput.MoveBits` set the up and
down bits from `aForward` against 3000 in every mode, so in slow mode the spy grabbed a ladder and hung at its foot
(ClubE's ladder: the sweep's spy hung 45 s in `s_OnLadder`). The decoded native (`NativeComputeControlVariables`,
editor 0x111f37d0; spec/native/input.md rule 3.2) uses that test only when walking, falling or a merc; a climbing spy's
stick `(aStrafe, -aForward, 0.01)` is normalised and compared on the screen with the climb's direction at 0.45, so the
speed mode does not matter. The port now does that, with the screen's up as the direction (the projection is open
question 2 of the spec). Tests: `MapSweepTraversalTests.A_spy_climbs_the_ClubE_ladder_with_the_forward_key` (slow:
climbed 6 uu before, over 250 after; fast passes both ways) and `SlowClimbTests` (synthetic ladder, up and down in
slow mode; walking keeps the 3000 test).

### Fixed: !next, !prev and Random Map could land on a tutorial or a test level

The session moved through every listed map (`MapCatalog.ListMaps`, folder order), which holds the tutorials (TSP,
TME), our TestRange and the fixtures: a player typing `!next` on TermD went to TestRange, and Random Map could
draw TSP21 (it has a Story entry). The original's list (`GetMapList`) and Reloaded's lobby never show those.
`!next`, `!prev` and Random Map now use `MapCatalog.RotationMaps()`, the host list in Reloaded's order, and the
lobby room lists the same maps under the original's names. Test: `MapRotationTests` (two players over the network
in a match on TermD type `!next`: TestRange before, VertE after; `!prev` gives StroD).

### Listed: the bots (src/OpenScct.Game/AI)

- **No Deathmatch goals**: mercs stand still and spies idle on every D map.
- **Walk graph islands**: on 21 maps a start's node reaches under 2% of the graph although a player walks out
  (PolaE: the merc wanderer got 2174 uu out; MissE: 2907). Causes seen: ladders, fences, poles and hand-over-hand bars
  have no links; a drop whose edge is a hull face (MissE stairs) is not linked.
- **Objectives out of reach** on the spy graph: 64 triggers on 12 maps (on the E maps 21 bomb spots, 19 terminals
  and 11 Story disks; 13 on StroD), the same causes.
- **Failing plans are expensive**: an unreachable goal floods the whole graph on every replan. StatE runs at 7 ms a
  tick with bots and 0.5 ms without; FactE 3.3 ms.

### Open questions (need the original)

- The order of maps whose names are the same without the variant word (Aquarius, Enhanced Aquarius): the catalog
  keeps the map code order; Reloaded's tie-break is not decoded.
- Reloaded's names for maps missing from its table (OrphT): its name cache holds "Enhanced Orphanage 3v3", which is
  the map's `MapName` with each word capitalised; the catalog does the same, but where Reloaded takes it from is not
  decoded.
- The tutorial order: `Tuto_MapsList` reads `TutoMapsA` and `TutoMapsD`, which no script or ini fills; the catalog
  orders them by name (TSP20 to TSP23, then the exam TSP29), which matches the page's rows.

- MissE / MissD merc stairs: whether the starts or the stairs' collision are where the original has them (the
  starts are about 140 uu above the hull's slope, and the slope ends at the bunker door). A capture of a merc spawning
  there in the editor's Play Level would settle it.
- `Level.SpawnActor` places pawns without a placement check (no FindSpot), which matches UE2 for actors without
  `bCollideWhenPlacing`; whether SCCT's spawn of a pawn runs FindSpot is not decoded. The sweep found no start where
  a spawned pawn ends inside geometry.
