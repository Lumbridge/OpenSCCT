# Launcher

`OpenSCCT.Launcher.exe` installs, updates and starts OpenSCCT. It reads GitHub Releases of the repository
(default `Lumbridge/OpenSCCT`), installs each game build in its own folder, switches between them in one step and
keeps the previous build for rollback. It can also update itself.

Releases carry our code builds only. Game content (maps, characters, UI) is built on the player's machine from
their own Splinter Cell Chaos Theory install. Maps can also be downloaded already converted from the
OpenSCCT-Content repository (docs/CONTENT-PACKS.md). The game does this for a map the player lacks, from a hub
pad's laptop or while queued on a pad. Extra maps made by the community can still be downloaded in their original
form from the SCCT-Maps repository (see "Downloaded maps").

## Projects

```
src/OpenScct.Launcher.Core/     update logic, no UI (net10.0)
  Versioning/SemVersion.cs        Semantic Versioning 2.0.0 parse and compare, "v" tags accepted
  GitHub/                         release list with ETag cache, rate limit and offline handling; asset requests
  Update/ReleasePicker.cs         newest release, game zip, launcher exe and checksum asset by name
  Update/Checksums.cs             checksums.txt and <asset>.sha256 parsing, SHA-256 of a file
  Update/ResumableDownloader.cs   download to .partial, resume with Range, restart when the server ignores it
  Update/UpdateService.cs         check, download, verify, install, switch, wait for a running game
  Update/BuildFiles.cs            the build's file list (per-file SHA-256 and the release asset holding each file)
  Update/DeltaInstaller.cs        install from the file list: reuse files already installed, download only the rest
  Update/LauncherSwap.cs          self update: stage .new, swap on restart, clean up .old
  Install/                        folder layout, install.json manifest, side by side install, rollback, prune
  Settings/                       launcher.json, the token store (DPAPI through ISecretProtector)
  Launch/                         the Play command line, running game detection
  Content/ContentBuildPlan.cs     "Build game content from your install": the builder's command line, run and progress
  (downloaded maps: src/OpenScct.Maps, shared with the game; see "Downloaded maps")
src/OpenScct.Launcher/          WinForms app (net10.0-windows): main window, settings, DPAPI, theme
tests/OpenScct.Launcher.Tests/  tests for the core library (fake HTTP handler, temp folders)
tools/release/New-Release.ps1   packs a build into release assets and prints the gh command
tools/release/Update-Launcher.ps1  swaps a new launcher onto the latest release (no game version)
```

The app has no third-party packages: WinForms, `HttpClient`, `System.Text.Json`, `ZipFile` and `ProtectedData` all
come with .NET 10. The test project uses the repository's pinned xunit.v3 versions (`Directory.Packages.props`).

## Version and build number

The version is `VersionPrefix` in `Directory.Build.props` (0.8.45; a release passes `-p:Version`), and the build number
is the UTC timestamp of the commit the build is made from, `yyyyMMdd.HHmm` (`Directory.Build.targets`). Both are stamped
into every assembly; `OpenScct.Net.Discovery.BuildInfo` reads them back for the server browser, the master and the
menus, which show "OpenSCCT 0.5.0  build 20261026.1737 (a82eaa0)" in the bottom-right corner of the main menu and
the options.

**The launcher has its own version**, `LauncherVersion` in `Directory.Build.props` (0.8.12), apart from the game's.
Bump it only when the launcher changes (`src/OpenScct.Launcher`, `src/OpenScct.Launcher.Core`, `src/OpenScct.Maps`,
the icon). A game release carries the same launcher exe again, so players' launchers update themselves only when there
is a new launcher. The launcher reads the version of a release's launcher from the asset name, not from the release
tag (launchers up to 0.8.6 compared the tag, so every game release updated them).

## Build and publish

```
dotnet build OpenSCCT.slnx                                         builds the launcher with everything else
dotnet test --project tests/OpenScct.Launcher.Tests                the launcher tests
dotnet publish src/OpenScct.Launcher -c Release -r win-x64
```

`-r win-x64` turns on the publish settings in the project: self-contained, single file, compressed, ReadyToRun.
The result is one `OpenSCCT.Launcher.exe` in `src/OpenScct.Launcher/bin/Release/net10.0-windows/win-x64/publish/`
that runs without an installed .NET, stamped with `LauncherVersion`: the launcher compares it with the version in a
release's launcher asset name to decide whether it needs to update itself. A framework-dependent build is much smaller (add
`--self-contained false -p:PublishReadyToRun=false`) but needs the .NET 10 Desktop Runtime on the player's machine.

## Making a release

1. Export the game from a pushed commit (Godot, preset "Windows Desktop", to `local\build\OpenSCCT.exe`).
2. `tools\release\New-Release.ps1 -Version 0.2.0`. The launcher is built only when `LauncherVersion` has not been
   made before; its exe and the git trees of its sources are kept in `local\release\launchers\<version>\`, and later
   releases attach that same exe. A release whose launcher sources changed under an old `LauncherVersion` is refused:
   bump it.
3. Edit `local\release\0.2.0\notes.md`, then run `local\release\0.2.0\publish.ps1`: it uploads the game files to
   the `game-files` release in batches (made the first time) and then creates the draft version release with the
   launcher only (run it again to retry a failed upload).
4. Check the draft on GitHub and publish it. The launcher ignores drafts, and the next release's delta is made
   against the newest published one.

**Launcher-only changes** need no game version, which would make players download the game and prepare their game
files again. Bump only `LauncherVersion`, commit and push, then run
`tools\release\Update-Launcher.ps1 -Notes "- what changed"`: it builds the launcher (or reuses the kept copy), puts
its checksum in `game-files`, replaces `OpenSCCT.Launcher.exe` on the latest version release with the new label, and
adds a "Launcher <version>" section to that release's notes. Players' launchers see the higher label and update
themselves. Changes to the content builder (`content/`, `tools/scctpkg`) ship with a game version, never this way.

The script refuses a build that has original game file types or converted content folders (`maps`, `svmap`,
`shared`, `.content`, `packs`) in it, runs `tools/ci/no_game_data.ps1 --all` on the staged files, leaves out
`override.cfg` and logs, adds `openscct-build.json` (version, commit, dirty flag, time) and writes two sets of
assets. Players see one download per version; everything else is the launcher's business.

**The version release** (`v<version>`, the game's version) holds only `OpenSCCT.Launcher.exe`, labelled
`OpenSCCT Launcher <launcher version>`. The name has no version, so a player's copy keeps one name across updates; the
launcher reads the version from the label. Its checksum is `OpenSCCT.Launcher-<launcher version>-win-x64.exe.sha256` in
the game files release. Older releases named the exe by version (`OpenSCCT.Launcher-0.8.7-win-x64.exe`); those still
work.

**The game files release** (tag `game-files`, one for every version, a prerelease so it is never "Latest"):

| Asset | What |
|---|---|
| `OpenSCCT-<version>-win-x64-files.json` | the build's file list for delta updates (below) |
| `<sha256>.gz` | one gzipped file of the build per new file content of 256 KB or more |
| `OpenSCCT-<version>-win-x64-pack.zip` | the smaller new file contents, one zip entry per SHA-256 |
| `<asset>.sha256` | `<sha256>  <asset name>` for the file list and the launcher |

The launcher looks for each asset in the version release first, then in `game-files` (asked for by tag when it is
older than the list's first page), so an old-style release with `checksums.txt` and a full game zip in the version
release still installs. An asset without a published checksum is never installed. The tag is the version with a `v`
in front (`v0.2.0`, `v0.3.0-beta.1`); a version with a prerelease part is also published as a GitHub prerelease, and
the launcher skips prereleases unless "Include prereleases" is on. `game-files` is not a version tag, so it is never
picked as one. GitHub allows 1000 assets per release; `publish.ps1` stops before `game-files` passes 990.

The script publishes nothing and does not change the repository's visibility. The repository is public, so players
need no token; a token (below) is only for a private fork.

## Folders

Everything the launcher writes is under `%LOCALAPPDATA%\OpenSCCT`:

```
install.json          current version, previous version (rollback), a pending version
launcher.json         owner, repo, prereleases, check on start, original game path
token.dat             GitHub token, encrypted with DPAPI for the current Windows user
versions\0.2.0\       one exported build per version
downloads\0.2.0\      downloads in progress (.partial with a small identity file) and finished zips
cache\                the last release list and its ETag, and a rate limit note
content\              game content built from the player's install (later)
maps\                 maps downloaded from SCCT-Maps: installed.json, game\ (their files), downloads\
```

The game's own user data (settings, key bindings, saves) is Godot's `user://`, in
`%APPDATA%\Godot\app_userdata\OpenSCCT`. The launcher never writes or deletes there. Deleting old versions only
works inside `versions\`, refuses any other path, and removes junctions or links without following them.

## Delta updates

The game is exported with its pack beside the exe (`OpenSCCT.pck`, `binary_format/embed_pck=false` in
game/export_presets.cfg), so the engine exe (about 110 MB) stays the same from build to build and a typical update
is the pack plus our own DLLs.

Each version has `OpenSCCT-<version>-win-x64-files.json` in the `game-files` release:

```
{ "format": 1, "version": "0.6.0", "commit": "...",
  "files": [ { "path": "OpenSCCT.pck", "size": 21733120, "sha256": "9c5a...",
               "blob": { "tag": "game-files", "name": "9c5a....gz", "size": 8012233 } } ] }
```

Every file of the build is listed with its SHA-256 and the release asset that holds it, gzipped and named by the
hash (small files are members of a version's pack zip). `New-Release.ps1` reads the newest published version's
list (`-BaseTag` picks another, `-NoDelta` none): a file whose hash is there keeps pointing at that older asset; only
new content becomes a new blob. Each blob is uploaded once and stays in `game-files`.

The launcher (`DeltaInstaller`) hashes the files of the installed current and previous versions (only files of a
size the list has), copies every file it already has into the new version's staging folder, downloads the missing
blobs (resumable, from whichever release holds them; a release older than the list's first page is asked for by
tag), unpacks each while checking its SHA-256, and renames the folder into place as before. A fresh install
downloads every blob once. The file list itself is checked against its `.sha256`.

There is no full game zip any more: **never delete the `game-files` release or assets in it** that a published
version's list points at. Deleting an old version release is fine; its files stay in `game-files`.

## How an update works

1. **Check.** `GET /repos/<owner>/<repo>/releases` with `If-None-Match` set to the cached ETag. A 304 costs no rate
   limit. Offline, timed out, 4xx or 5xx: the cached list is shown with the reason. A rate limit answer (403 or 429
   with `X-RateLimit-Remaining: 0` or `Retry-After`) is remembered until its reset time and no request is sent
   before then. Anonymous use has 60 requests an hour, which a launcher that checks on start does not come near.
2. **Pick.** The highest semantic version among the tags (drafts never, prereleases on request). The file list
   (or, without one, the game zip) and its checksum are found by name. With a file list, steps 3 to 5 run per
   missing file (see Delta updates).
3. **Download.** Without a token through the public download link (API requests are limited to 60 an hour for
   anonymous callers, fewer than a fresh install needs); with a token through the asset's API URL with
   `Accept: application/octet-stream` (works for a private repository;
   HttpClient drops the token when GitHub redirects to its storage host). The file goes to `<name>.partial`; an
   identity file (asset id, size, upload time) makes sure a partial is only resumed for the same upload. Resume is a
   `Range` request; a server that answers 200 instead starts the file over; a dropped connection is retried up to
   four times, resuming each time.
4. **Verify.** SHA-256 of the whole file against the published checksum. A mismatch deletes the download.
5. **Install.** Unzip into `versions\.staging-<version>-<id>`, check for `OpenSCCT.exe`, rename to
   `versions\<version>` in one step. Existing version folders are never touched, so a running game keeps its files.
6. **Switch.** One atomic write of `install.json` (temp file and rename): the new version becomes current and the old
   one becomes previous. If a game is running, the new version is recorded as pending and the switch happens as soon
   as no game process is left (checked every 3 seconds and on the next start).
7. **Prune.** Folders other than current and previous, and leftover staging folders, are deleted, except folders a
   running game uses. A game whose exe path cannot be read counts as using every version.

**Rollback** swaps current and previous in `install.json` (only while no game is running).

**Self update.** When the release has a launcher (see above) and its version is newer than the
running launcher, the launcher fetches it right after the update check ("Update launcher" retries a failed
download). The exe is downloaded, verified and copied next to the
running one as `OpenSCCT.Launcher.exe.new`. "Restart to finish" renames the running exe to `.old` (Windows allows
renaming a running exe), moves `.new` into its place, starts it with `--after-update` and closes. A launcher still
under an old versioned name (`OpenSCCT.Launcher-0.8.0-win-x64.exe`) takes the plain name `OpenSCCT.Launcher.exe`;
any other name is kept, and so is the old name when `OpenSCCT.Launcher.exe` is already there. A launcher started with
`--after-update` by an older one that kept its versioned name renames its own exe the same way. The new process
deletes `.old` (its own, or the `OpenSCCT.Launcher-*.exe.old` a renaming update left), and finishes or undoes a swap that stopped half way. A launcher in a read-only folder (Program Files)
says so instead of updating; it never asks for elevation.

## Play

Play starts `versions\<current>\OpenSCCT.exe` from its own folder. A plain start opens the main menu, so no
arguments are needed. When the original game path is set it is passed as `-- --svm-game-root=<path>` and as
`SVM_GAME_ROOT` (key layouts), and the launcher's `content\` folder is passed as `--svm-content=<folder>` (see Game content).

## Settings and the token

Settings: owner and repository, include prereleases, check on start, the original game install folder (with a
check that it has a `System` folder), and links to the launcher folder and to the game's user data folder.

The token is only for a private repository. A fine-grained token with read-only "Contents" access to the one
repository is enough. It is encrypted with `ProtectedData` (DPAPI, `CurrentUser` scope, with fixed entropy) into
`token.dat`, never written in plain text, never shown again, and can be removed. A token that cannot be decrypted
(another user or machine) counts as none. Without a token the launcher calls GitHub anonymously.

## Game content

The original game is optional. With a Splinter Cell Chaos Theory install, maps, characters, UI, sounds and effects
are converted on the player's machine from it. Without one, they are downloaded from OpenSCCT-Content
(docs/CONTENT-PACKS.md) into `%LOCALAPPDATA%\OpenSCCT\packs`. Content converted here always wins over downloaded
packs.

**Without an install** (`PackedContent`): step 1 says the files will be downloaded, and step 3's button is
"Download game files". The first download fetches the shared packs (core, chars, audio) and every playable `stock`
map, about 1.9 GB. Later downloads fetch only newer copies of the shared packs and of maps already installed, so a
map the player removed stays removed. Other maps come from the Maps window, or in game when a lobby or the hub picks
them. The launcher fetches the index on each update check. When a newer pack is published, step 3 asks for a
download before PLAY. PLAY needs the three shared packs. The game is then started without `--svm-content` and
`--svm-game-root`, and finds the packs itself (`ContentPaths.Shared`). Downloaded files are in English, and the
Language setting applies only to content built from an install. The stock key layouts come from the core pack's
`ui/keys/` (the ui step copies `System\Default_kbd_0.ini` and `_1.ini` there). A pack published before the ui step
v4 has no key layouts, and the game then uses its smaller built-in layout.

**The builder ships with each build.** `tools/release/Build-ContentTools.ps1` (run by `New-Release.ps1`; skip with
`-SkipContentTools`) puts `contenttools\` into the game build:

```
contenttools\python\   CPython 3.10 embeddable package (python.org) with numpy, Pillow, jsonschema (PyPI wheels,
                        content/requirements.txt), search path set in python310._pth
contenttools\repo\     our tracked sources in the repository's layout: content\svmbuild, tools\scctpkg\scctpkg,
                        re\formats\scctformats, spec\svmap
```

Only git-tracked files are copied. scctpkg's game-derived `class_defaults.json` is not shipped: svmbuild writes it
from the classes step's `shared\classes.json` (`svmbuild/sources/classdefaults_dump.py`, the class names of the
upstream dump with the values read from the player's install; equal to the upstream file for all 274 classes). The
builder files are part of the build, so the delta updater only downloads them again when they change.

**The launcher runs it** (`ContentBuilder`, `ContentBuildPlan`) when the player presses "Build content", and on its
own right after a game install or update whose content is not current:

`contenttools\python\python.exe -u -m svmbuild classes chars audio ui maps-catalog renderpack fx maps audio-map --maps all --game "<install>" --out "%LOCALAPPDATA%\OpenSCCT\content" --jobs <half the cores, at most 6> --report "<content>\build-report.json"`

No window opens; the output goes to `content\build.log`, and the progress bar counts finished units (each global
step, and each map per map step). "Stop" kills the builder and its workers; the builder's cache (`content\cache\`)
keeps what was finished, so the next build resumes, and a build after an update redoes only steps whose code or
inputs changed. When it ends the launcher writes `content\content-build.json` (version, install, ok, maps,
failures). The content is current when that file names the current version and the same install.

**Language.** Settings has a Language list: English, French, German, Spanish and Italian, those the install has text
files for (`System\_PC_\GUI.<code>`). The launcher passes `--language fra` (and so on; nothing for English) and keeps the
language in `content-build.json`, so content built in another language is not current; changing the setting builds
again. The builder (`scctformats.classdefaults.language`, `SVM_LANGUAGE` for its workers) reads every text file in
that language, falling back to `.int`: the class defaults, the UI strings and each map's own text. The map converter
puts `<Map>.<code>` over its actors' localized properties (zone tips, missions, alarms) and `<Map>-i.<code>` over
SMapSettings (briefings, ModesStrings), as the original does at load (`svmbuild/mapconv/localization.py`): retail
packages store French text, so without this a Steam copy showed French zone tips. Our own menus are English only.

**The game finds it** through `--svm-content=<content>` (Play passes it; `SVM_CONTENT` also works): shared packs from
`<content>\shared`, converted maps from `<content>\svmap` (`OpenScct.Server.ContentPaths`). The maps the game ships
itself (the hub with `hubmap\hub.json`, TestBox) are in `maps\` next to the exe, copied from `fixtures\` by
`New-Release.ps1`. A checkout still finds `.content` and `fixtures` as before. With an install, Play refuses to start until
a content build has finished once.

**Which install.** The builder reads a Versus folder (`System\_PC_` and `Packages`). The launcher does not look
for one: a new player gets the downloaded game files, and uses an install only by choosing its folder in step 1 or
Settings (`GameLocator.Normalize` takes the game root, its Versus folder, or any folder inside it such as `System`;
`GameLocator.Find` still knows the Steam libraries and common folders but the launcher no longer calls it). Checked on
the Steam copy (2026-10-26): all 21 of its maps and every shared pack convert and validate, about 470 MB in 45 s;
its maps are the retail set (AQU03, BNK11, ...), not Enhanced SCCT Versus's 83. Props of maps the install does not
have (the SteeE crane, `sqt13_anm`, and the other map anim packages) are skipped.

A fresh player: install the launcher and press Install. The launcher downloads the build, then builds the content
from the game when it finds it, or downloads it when it does not, and then Play works.

## Downloaded maps

The **Maps** button (bottom row) lists every playable map of
[OpenSCCT-Content](https://github.com/Lumbridge/OpenSCCT-Content) (docs/CONTENT-PACKS.md). These maps are already
converted, so Download puts them in `packs\` and they are ready to play without "Prepare the game files". A map
the player's own converted content has shows as "Converted from your game". Players with no converted game files
get the shared packs with their first map. Maps an older launcher downloaded from SCCT-Maps still show as "old
download". Removing one prepares the game files again; nothing new is downloaded from SCCT-Maps there. The lobby's
missing-map download (docs/ONLINE.md "Missing maps") uses OpenSCCT-Content too.

Before 0.8.26 the Maps button opened a list of the maps in [SCCT-Maps](https://github.com/Lumbridge/SCCT-Maps),
read the way the SCCT Map Manager reads it (its `docs/publishing.md`): original maps (`release/<Package>/<version>`,
newest version, names from `release/names.json`), community (with `community/_shared`), enhanced, recovered and
ported maps, the ports released as assets (`asset-catalog.json`), and the stock SCCT Versus 4.0 maps
(`stock/Packages/Maps/*.sdc`, category Stock, listed as `stock/<Code>` and named after their asset pack, such as
"Aquarius (AquaE)"; Entry and Lobby are not listed). Editor asset packs and editor-only scenery (no
`Packages/Maps/*.sdc`) are not listed, and only the files the game plays with are downloaded (Maps, Textures,
StaticMeshes, Sounds, Animations, `System/_PC_` text; never MapsEd).

**Stock asset packs.** The `assets/svm-*` entries of `asset-catalog.json` (every texture, mesh, sound and animation
package of SCCT Versus 4.0, in 36 packs on `svm-*-vX.Y.Z` releases) are never listed as maps. Their files join the
package pool where the repository's folders have no copy, so any downloaded map (stock, enhanced, community) takes a
stock package it imports from them when the install lacks it, file by file, not whole packs. A stock map also takes its
own `<Code>-i.utc` (menu pictures and text) and `<Code>.uas` (sounds) from them, which the map file does not import.
These packs list plain SHA-1 hashes where the tree and the ports list Git blob ids; a download is accepted when it
matches either (`MapPaths.MatchesHash`). The install is still needed to build content (System scripts, menus and
Reloaded's map tables are not in the packs).

- One GitHub API call per refresh (the repository's tree, pinned to its commit); files then come from
  `raw.githubusercontent.com` at that commit or from release assets, which are not rate limited. No token.
- Each file is checked against its size and hash (Git blob id, or plain SHA-1 for the stock asset packs) before it is put in place; a bad file is
  deleted. Paths are checked (no `..`, devices, links) before anything is written.
- Files go to `%LOCALAPPDATA%\OpenSCCT\maps\game\`, laid out like an install. The player's game folder is never
  written. Maps share files: a file is downloaded once and deleted with the last map that lists it. When two maps list
  different contents for the same file, the last one downloaded wins.
- A map whose codes the install already has shows "Already in your game" and cannot be downloaded: the install's copy
  is the one used.
- The list shows Downloaded, Update available (the repository's files changed) and downloaded maps no longer listed,
  which can still be removed.

**Building them.** `ContentBuildPlan` passes `--addons <maps\game>` to the builder when there are downloaded maps and
the version's builder has `svmbuild/gamefiles.py` (an older builder would stop on the option; it builds without them
and the launcher says so). The builder (`svmbuild.gamefiles`, `SVM_ADDONS` for its workers) reads that folder as a
second layer under the install: the install's file always wins, so a map that ships its own copy of a stock package
(`SWeapon.ukx`, `AQU_anm.ukx`) never changes stock content. The maps list, maps-catalog, maps, audio, audio-map,
renderpack (stored mips and vision modifiers) and maps-preview steps read it; classes, chars, ui and fx read the
install only. Addon files have `addons:` logical paths in the cache, so only steps whose inputs changed run again.

`content-build.json` keeps the downloaded maps' fingerprint (`downloadedMaps`); downloading, updating or removing a
map makes the content not current, and closing the Maps window prepares the game files again when they were ready
before (the cache converts only the new maps, plus renderpack and maps-catalog).

The catalogue, downloads, library and the builder runner are in `src/OpenScct.Maps`, shared by the launcher and
the game. Play passes the maps folder as `--svm-map-downloads=`, so in a lobby the game can fetch a map the host
downloaded and the player lacks (docs/ONLINE.md "Missing maps").

## Open

- Not run yet: the tests, a publish of the single-file exe, and a real round trip against a GitHub release (draft or
  a test repository).
- Not run yet: a content build with the packed builder (its Python and wheels have not been downloaded on this
  machine), and a fresh install through the launcher end to end.
- The content cache roughly doubles the disk the content takes (about 2 GB of content for all maps).
- No stall timeout inside a download body: a connection that hangs without closing waits until the user cancels or
  closes the launcher. The partial is kept for the next try.
- No cancel button yet; closing the window cancels a running download.
- Release notes are shown as plain Markdown text.
- The exe is not code signed, so SmartScreen warns on first start.
