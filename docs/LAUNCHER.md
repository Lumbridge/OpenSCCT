# Launcher

[Documentation](README.md)

The Windows and native Linux launchers install, update and start OpenSCCT. They download releases from
[OpenSCCT/OpenSCCT](https://github.com/OpenSCCT/OpenSCCT/releases/latest) and update themselves.
The Linux launcher uses the same download, verification, content and version-management code as Windows.
See the [Linux guide](LINUX.md) for its portable package, desktop installation and system requirements.
Linux assets must be published alongside the game before players can install that version; older
Windows-only releases do not include a Linux download.

## Install and play

Download **OpenSCCT.Launcher.exe** on Windows or **OpenSCCT.Launcher-linux-x64**
on Linux from the [latest release](https://github.com/OpenSCCT/OpenSCCT/releases/latest).
Put it in a writable folder and open **Games**. **Originals** contains PlayStation 2 and PC / Steam.
**Modernisations** contains OpenSCCT Godot and OpenSCCT Unreal, which rebuild Versus with new engines, graphics and features.
**Mods** shows the choices for the selected edition and **Settings** opens its configuration.

| Choose | Set up and play | Switch mods |
|---|---|---|
| **PlayStation 2** | Select a local Chaos Theory PS2 ISO and the PCSX2 executable. Configure your BIOS in PCSX2. **Play in PCSX2** starts the disc. | Choose a separately patched ISO of the same disc release. **Use original disc** restores the base selection. |
| **PC / Steam** | The launcher looks for Steam app 13570. You can also choose an installed game folder. **Play campaign / co-op** starts the main game. **Play Versus** starts the multiplayer executable. | Choose a separate modded copy under **Mods**. A Versus-only profile such as Enhanced or Reloaded keeps the base campaign available. A full-game profile can supply a modified campaign. Select the base installation again to play stock. |
| **OpenSCCT Godot** | Install the Godot client. The launcher finds the original game in Steam or lets you select its folder. Build the Versus content from that installation, then choose **Play**. | **Maps** offers additional maps, from the community catalogue. |
| **OpenSCCT Unreal** | On Windows, install the modern Versus client and build its required content from your Steam game. The launch panel shows its next install, update or play action. | Select a parent folder containing compiled community SVMap directories. Enable or disable those maps before starting the client. |

Enhanced and Reloaded apply to original PC Versus. Enhanced includes Reloaded.
See the [Enhanced releases](https://github.com/Joshhhuaaa/EnhancedSCCTVersus/releases)
and [Reloaded features](https://allypal.github.io/SCCT_Versus_Reloaded/features/).
See the [PS2 setup guide](editions/ps2.md) for disc compatibility and PCSX2 setup,
[Unreal map mods](UNREAL-MAP-MODS.md) for the required folder layout, and the
[Linux guide](LINUX.md) for Steam, Wine and edition support.

### Godot setup

Select **OpenSCCT Godot** under **Modernisations**. Install Chaos Theory in
Steam, including its Versus files. The launcher checks the source folder and
builds OpenSCCT's maps, characters, sounds and shared assets in its own data
folder. It reads the stock game in place. Keep a separate Enhanced or Reloaded
copy for playing modded original PC Versus.

The Steam depot includes 11 multiplayer maps and eight training maps. The two
menu packages are also part of the source. Once the build completes, **Play**
starts Godot using those local files. **Maps** offers additional downloads.
See the [stock map list](../shared/SteamVersusMaps.txt) for the package IDs.

If you played an earlier Godot build that downloaded shared packs, select the
Steam installation and complete the local build once. Your saved settings and
optional maps stay available. A completed local build is selected only after
the converter has produced and checked its output.

Managed products show their release information, update progress and available
actions for the selected product. **Full changelog** opens its release notes.

## Optional Windows editor

Select **OpenSCCT Editor** under **Tools**. Its blue theme
identifies the editor, and **Set up Editor** starts its optional installation.

**Set up Editor** installs the editor and checks the local Steam content build. The editor reads the same stock maps and assets as the modern clients. **Open Editor** starts
it when setup is ready; **Use current version** remains available when an optional update exists or
an update fails. **Retry update** remains available after a failure. Game and editor versions and
preview preferences are independent.

Updates can be started in the launcher or in the editor. The launcher delegates editor updates to the
editor's packaged `ose` tool, preserving its verified staging, startup probe and recovery. Save and close
the editor and its command-line tools before updating from the launcher. Cancellation is available
during staging; once replacement starts, the helper finishes or recovers before the launcher closes.

You can change tabs during a download: progress and cancellation continue to refer to the operation
that started it. Website Join links select OpenSCCT Godot and wait for an active operation. Launcher self-updates
remain available on every tab. Editor Help and release links go to the editor project.

**Settings** belongs to the selected tab. Each tab has its own startup update check, preview
preference, folder shortcuts and installation actions. Existing installations initially inherit the old
startup-check choice for every product; changing it in one tab then leaves the others unchanged.

In **Game settings**, **Import from Enhanced SCCT Versus** queues an import of the player's Enhanced settings
for the game's next start. **Choose Enhanced folder…** takes any folder of the install (everything: sensitivities,
key bindings, display, HUD, voice, volumes, gear); **Bindings only (no install)** takes just the profile and key
layouts the original keeps in ProgramData. Saving writes `enhanced_import.request` (the folder, or empty) into the
game's user data (`EnhancedImportRequest`); the game carries it out once at start, saves the result and removes the
file (`EnhancedImportStartup`), and its Options note says what was imported. The same import is in the game's
Options as **Import from Enhanced** ([MENUS.md](MENUS.md)), staged there until Apply.

In **Editor settings**, **Uninstall editor…** removes the launcher-managed editor application and keeps
projects, editor settings/autosaves, the game and shared assets. Close the editor and its command-line tools
first; finish any interrupted update before uninstalling. Extra or modified files inside the installation
are kept in a separate folder, linked in the completion message. **Set up Editor** can install it again.
Independently downloaded editor installations are not adopted or removed.

In **Game settings**, **Remove game and shared files…** removes all installed game versions and downloaded
assets/maps, keeping settings and saves. The Editor application, its projects and the Unreal application have independent installations. If you remove their local base content, run Steam setup before opening them again. Close active clients before changing shared content.
**Maps** manages optional community downloads.

See the [Linux guide](LINUX.md) for the controls available on Linux.

Integration requires an editor package advertising launcher protocol 1 with its
`OpenSCCT-Editor-<version>-win-x64.launcher.json` release asset and `version.json` metadata. Until a compatible
editor release is published, the launcher reports that no compatible download is available. Distribute
the compatible editor release before launcher 0.8.45; neither is published by building this change.

## Unreal tab

The Windows library entry **OpenSCCT Unreal** installs, updates and starts OpenSCCT Unreal, the Unreal Engine build of the
game. It uses the same launch panel as the other tabs, with an orange colour scheme. It has its own versions,
preview preference and startup update check. OpenSCCT Unreal runs on Windows only.

**Where it installs.** The build goes in `%LOCALAPPDATA%\OpenSCCT\unreal\`, which is laid out like the launcher
folder: `install.json`, `versions\<version>\`, `downloads\` and `cache\`. The Godot game's files are not touched.

**Where releases come from.** By default the releases come from the GitHub repository
`OpenSCCT/OpenSCCT-Unreal`, which must be public. `UnrealRepository` in `launcher.json` (`"owner/name"`)
replaces it; an invalid value is ignored. No GitHub account or token is used.
If the repository cannot be read (while it is still private, GitHub answers as if it did not exist) or has no
compatible release, the tab says
"No OpenSCCT Unreal release is available yet." This is not an error.

OpenSCCT-Unreal's `Tools/Package-Release.ps1` makes the releases (see its `docs/RELEASING.md`). Each version
release, tagged `v<version>`, carries:

| Asset | Purpose |
| --- | --- |
| `OpenSCCT-Unreal-<version>-win-x64.zip` | The whole build, in one `OpenSCCT Unreal` folder |
| `OpenSCCT-Unreal-<version>-win-x64.zip.sha256`, `SHA256SUMS` | Checksums of the ZIP |
| `OpenSCCT-Unreal-<version>-win-x64.launcher.json` | `{"launcherProtocol":1,"product":"OpenSCCT Unreal",...}` |
| `OpenSCCT-Unreal-<version>-win-x64-files.json` and its `.sha256` | The file list, in the game's format 1 |

The file contents (`<sha256>.gz`), packs of small files (`OpenSCCT-Unreal-<version>-win-x64-pack-<hash>.zip`)
and binary patches (`<from>-<to>.osdiff`) are kept in the shared `unreal-files-1` prerelease.

The tab offers a release only if it has a `launcher.json` and either the ZIP or the file list. It offers
prereleases (marked on GitHub or by a prerelease version) only when the tab's preview setting is on. Before
installing, the launcher reads the release's `launcher.json`. It requires `launcherProtocol` 1 and `product`
`OpenSCCT Unreal`; for any other release it says "This OpenSCCT Unreal version needs a newer launcher."

**How it updates.** Updates use the same updater as the game (see
[Release assets and delta updates](#release-assets-and-delta-updates)). It reuses unchanged files from the
current and previous versions and applies binary patches where they are smaller. It downloads the other files,
checks every file against its SHA-256 and builds the new version in a staging folder. A first install also uses
the file list: with nothing to reuse, it downloads every file. The ZIP is used only for a release without a file
list, or when a file's asset can no longer be found. Before it writes anything, the launcher checks that the disk
has room for the downloads and the new version folder, and says how much space is needed if not. If the current
version's folder is damaged (its `version.json` is missing or unreadable), installing the same version again
moves that folder aside and rebuilds it. If the game is running, the new version becomes current when the game closes. The running game is a process
named `OpenSCCTUnreal` or `OpenSCCTUnreal-Win64-Shipping` started from `unreal\`. Cleanup keeps the current and
previous versions. The launcher never updates itself from these releases.

**What it passes to the game.** The launcher starts `versions\<version>\OpenSCCTUnreal.exe` from its own folder,
with the completed local Steam build as its content root. If the required files are missing, select Chaos Theory's Steam folder and build again. With custom maps enabled, the
launcher also supplies the selected map root. Paths containing spaces are quoted
and paths containing a double quote are rejected. The launcher removes the
`SVM_CONTENT` and `SVM_GAME_ROOT` environment variables from the child process.
The build needs the Visual C++ x64 runtime, at least the version
of the installer it carries. When the runtime is missing or older (an old 2015-2019 runtime counts as older), the
launcher can run that installer (`Prerequisites\vc_redist.x64.exe /install /passive /norestart`). While an
update waits for the running game to close, the tab shows **Waiting for game to close** instead of **Play**.

The game handles its own account sign-in. Website Join links open the Godot entry. **Maps** offers additional community maps.

**Uninstall.** In **Unreal settings**, uninstalling deletes `unreal\` and, if you choose, `unreal-packs\`. It is
refused while the game runs. It keeps the Godot game, the content packs, the editor, `launcher.json` and the
game's own saved settings, which are outside the launcher folder. Force close ends only Unreal game processes
started from `unreal\`.

## Updates and troubleshooting

- **Update** installs a newer game build and any newer downloaded game files, including file-only updates.
  **Play current version** lets you defer updates for that launch.
  Play checks again for updates with a five-second timeout; a failed check still permits installed play.
- **Maps** downloads additional converted maps, ready to play without conversion.
- **Website** opens the player hub. **Full changelog** opens its changelog, with built-in release notes if
  the website or browser is unavailable.
- Downloaded game files and menus are in English.
- The launcher is unsigned, so Windows may show a SmartScreen warning on first launch.

Each Windows tab's settings includes its own update preferences, folder shortcuts and installation removal action.
Save and Cancel stay visible while the settings scroll on smaller screens.
The launcher gets updates from the public `OpenSCCT/OpenSCCT` repository without a GitHub account or token.

### Monitor

In the Windows launcher's **OpenSCCT Godot** entry, **Settings > Monitor** chooses the screen the game opens on. It is the
same setting as **Options > Display > Monitor** in the game: monitor 1 is the Windows primary screen and the
others follow from left to right. Use it when the game opens on a screen you cannot see. Close the game
before changing it.

### Graphics renderer

The game draws with Vulkan on Windows. Versions 0.8.99 to 0.8.125 also offered Direct3D 12 in the launcher and in
**Options > Performance**; that choice is gone. A profile or launcher that still names Direct3D 12 starts the game on
Vulkan (the game restarts itself once if an older launcher passes the old driver).

## Help and contributions

Choose **Help** for documentation, suggestions, existing issues and contribution guides.
The same window links to developer setup and pull requests. Help stays available while the launcher
downloads or prepares files, and opening it preserves the current progress or error message.

**Suggest a feature** and pull requests open on GitHub and require GitHub sign-in.
**Copy details** copies the launcher version, installed game version, build information and operating
system. If the browser cannot open, use **Copy link** or select the address and copy it
yourself. You can also select the version details manually if the clipboard is unavailable.

## Website Join links

The launcher registers `openscct://join/<server-id>` for the current Windows user when it starts.
Run it once after updating or moving it. Registration requires no administrator rights.
The handler requires launcher 0.8.26 and game 0.8.58 or later; Flatpak 0.8.58+ registers the same scheme.

Join links contain only a public server ID. The game uses its own account sign-in and asks for a password
when the server needs one. Browser sign-in does not sign in the game. A join waits for installation or
updates to finish, and for an active match to return to the main menu.

An already-running launcher or game receives the request through a same-user named pipe. The request
survives launcher self-update. A different configured master is not replaced automatically or sent
another site's credentials. See the [player hub guide](PLAYER-HUB.md) for the website flow.

## Folders

The launcher stores its files under `%LOCALAPPDATA%\OpenSCCT` on Windows and
`${XDG_DATA_HOME:-~/.local/share}/OpenSCCT` on Linux:

| Path | Contents |
| --- | --- |
| `install.json` | Current, previous and pending game versions |
| `launcher.json` | Startup update and preview-release preferences, and the Unreal release repository |
| `versions/<version>/` | One exported game build per version |
| `editor/` | Optional managed Windows editor, including its CLI and update recovery state |
| `editor-cache/`, `editor-downloads/` | Independent editor release cache, bootstrap downloads and external helpers |
| `unreal/` | The Unreal build: its own `install.json`, `versions/`, `downloads/` and `cache/` (Windows) |
| `unreal-packs/` | Map packs the Unreal build downloads by itself |
| `downloads/` | Completed and partial release downloads |
| `cache/` | Cached release list and rate-limit state |
| `content/` | Locally converted Steam builds and the marker selecting the completed build |
| `packs/` | Downloaded [content packs](CONTENT-PACKS.md) |
| `maps/` | Original-format maps downloaded by older launchers |

Game settings, bindings and saves live separately in
`%APPDATA%\Godot\app_userdata\OpenSCCT` (Godot's `user://`). The launcher does not delete that folder.
On Linux the game uses `${XDG_DATA_HOME:-~/.local/share}/godot/app_userdata/OpenSCCT`.
Deleting the launcher data folder removes installed builds and content; use it only when you intend to reinstall.

## Game content

Godot, Unreal and the Editor read their base assets and stock maps from the local build. Optional map packs
use the [content repository](https://github.com/OpenSCCT/OpenSCCT-Content).
See
[Content packs](CONTENT-PACKS.md) for their archive format and
[the content converter](../content/README.md) for build details.

## Build and publish

```powershell
dotnet build OpenSCCT.slnx
dotnet test --project tests/OpenScct.Launcher.Tests
dotnet run --project tools/launcher-smoke
dotnet publish src/OpenScct.Launcher -c Release -r win-x64
dotnet publish src/OpenScct.Launcher.Linux -c Release -r linux-x64
```

The Windows-only [layout smoke harness](../tools/launcher-smoke/README.md) checks isolated launcher states
and dialog layouts at 100%, 150% and 200% scaling, saving offscreen PNGs under
`local/launcher-smoke/artifacts`. It uses temporary installations without starting downloads or the game.

The publish command creates a self-contained `OpenSCCT.Launcher.exe` in
`src/OpenScct.Launcher/bin/Release/net10.0-windows/win-x64/publish/`. Players do not need .NET installed.
For a framework-dependent build, add `--self-contained false -p:PublishReadyToRun=false`; that build
requires the .NET 10 Desktop Runtime.

The Linux project uses Avalonia and publishes a self-contained single executable named
`OpenSCCT.Launcher-linux-x64`. Its managed and native libraries are bundled for self-update;
players do not need a system .NET installation. The GUI uses X11 (or XWayland on a Wayland desktop).

### Version and build number

[Directory.Build.props](../Directory.Build.props) defines `VersionPrefix` for the game and
`LauncherVersion` for the launcher. [Directory.Build.targets](../Directory.Build.targets) derives the
build number from the commit's UTC timestamp (`yyyyMMdd.HHmm`).

Bump `LauncherVersion` when launcher code, its shared dependencies or branding change. Game releases
reuse the previously built launcher when its version is unchanged. Packaging refuses changed launcher
sources under an already-published launcher version.

### Making a release

Each push to main prepares the Windows export and VPS packages in `ci`, while `linux-export` prepares Linux
and Flatpak for the same commit. These jobs run alongside the validation suite. Use the artifacts from the
successful workflows for that exact commit. To prepare a branch candidate before merging, dispatch `ci`
with `release_candidate=true`; that run also includes Linux and Flatpak preparation.
Download `openscct-windows-export` into `local/build` for the packaging step below. Use the existing versioned
launcher cache so the launcher checksum stays stable across game releases. The Linux release and Flatpak
artifacts are already packaged. The `openscct-vps` artifact works with
`tools/deploy/Deploy-Vps.ps1 -FromPackage <directory>`.

Review the release scope and draft before publishing. Prepare Editor or shared content separately when
included, and verify the deployed services after promotion.

1. Export the game from a pushed commit using Godot's **Windows Desktop** preset to
   `local/build/OpenSCCT.exe`.
2. Run `tools/release/New-Release.ps1 -Version <version>`.
3. Review `local/release/<version>/notes.md`, then run the generated `publish.ps1`.
4. Review and publish the draft release on GitHub. The launcher ignores drafts.

[New-Release.ps1](../tools/release/New-Release.ps1) prepares assets and a publishing script; it does not
publish by itself. It checks for original game files and converted content, excludes local overrides/logs,
and adds build metadata. Launcher binaries and source fingerprints are kept in
`local/release/launchers/<version>/` for reuse.

For a launcher-only update, bump `LauncherVersion`, commit and push, then run:

```powershell
tools/release/Update-Launcher.ps1 -Notes "- Describe the launcher change"
```

This updates the launcher asset, checksum and notes on the latest release without changing the game
version. Changes to the content builder ship with a game release instead.

### Release assets and delta updates

The version release (`v<version>`) carries `OpenSCCT.Launcher.exe`, labelled with its launcher version.
The shared `game-files` prerelease stores the files downloaded by the launcher:

| Asset | Purpose |
| --- | --- |
| `OpenSCCT-<version>-win-x64-files.json` | File paths, sizes, SHA-256 hashes and download locations |
| `<sha256>.gz` | New file contents of 256 KB or more |
| `<from>-<to>.osdiff` | Optional binary patch from an older version of a big file |
| `OpenSCCT-<version>-win-x64-pack.zip` | Smaller new files, with entries named by hash |
| `<asset>.sha256` | Checksums for the file list and launcher |

The updater reuses unchanged files from the current and previous installations, then downloads and
verifies missing files. Downloads resume where supported. It installs into a staging directory and
switches `install.json` atomically. A running game keeps its version; the switch waits until it exits.
Cleanup retains the current and previous builds and avoids paths used by running games.

**Keep the `game-files` release and every asset referenced by a published file list, patches included.** Older game
versions can depend on those files. The publishing script stops before the release reaches 990 assets.
`-BaseTag` selects a different delta base; `-NoDelta` creates a complete set of new blobs.

When that store is nearly full, use `New-Release.ps1 -StoreTag game-files-2` (or another new tag).
New blobs go to the new storage prerelease; reused blobs keep their original release tags.
The version release also carries the file list, its checksum and the launcher's checksum, which existing
launchers discover before looking in `game-files`. Delta lookup falls back to `game-files` for older bases.
Keep every earlier storage release and its referenced assets.

#### Binary patches

A file entry in the file list may also carry `patches`, binary patches to this file from older versions of it:

```json
"patches": [ { "from": "<sha256 of the old file>", "fromSize": 167849984, "tag": "<release>",
               "name": "<from>-<to>.osdiff", "size": 7746667 } ]
```

The list stays `format` 1, and older launchers ignore the field. The launcher also hashes installed files whose
size matches a patch's `fromSize`. When a file is missing but a patch's `from` file is installed, the plan
downloads the smallest such patch, provided it is smaller than the blob. Otherwise it downloads the full blob.
Patch bytes count in the download total. The launcher skips a patch that is missing from its release and uses
another patch or the blob, so this does not cause a full-zip install. If a patch does not apply (it is damaged,
or the base file is not the one it was made from), the launcher downloads that file's blob instead. It stops
only if the blob is also gone. Before writing anything, the install checks that the disk can hold the new
version folder and the downloads.

Patches are OSCTDIF1, bsdiff-style. They are written by OpenSCCT-Unreal's `Tools/ReleaseDelta`
(`diff <old> <new> <patch>`), the reference for the format. `BinaryPatch.cs` documents the layout and applies a
patch in-process. It checks the old file's size and SHA-256 first and the new file's SHA-256 at the end, and
deletes partial output on failure. Each patch applies to one older file; the launcher does not chain patches.

Older releases with a full game ZIP and `checksums.txt` remain supported. Drafts are ignored, and
prereleases require the corresponding launcher setting. Assets without published checksums are rejected.

### Self-update

A newer launcher is downloaded, verified and staged beside the running executable as `.new`.
**Restart to finish** swaps it into place and removes the old copy after restart. The launcher recovers
an interrupted swap. A read-only folder prevents self-update; it does not trigger an elevation request.

Older launcher settings remain readable, but their GitHub source, original-install path and conversion
language fields are ignored and removed on the next settings save. Any old `token.dat` is left untouched
and is no longer read or used.
Fork maintainers can change the release source in their own launcher build.

## Code and checks

| Location | Responsibility |
| --- | --- |
| [OpenScct.Launcher](../src/OpenScct.Launcher/) | Windows UI, settings, website links and self-update |
| [OpenScct.Launcher.Linux](../src/OpenScct.Launcher.Linux/) | Native Linux desktop UI using the shared installer/updater |
| [OpenScct.Launcher.Core](../src/OpenScct.Launcher.Core/) | Release selection, downloads, checksums, installation and launch |
| [OpenScct.Maps](../src/OpenScct.Maps/) | Shared map catalogue, pack downloads and content-building support |
| [Launcher tests](../tests/OpenScct.Launcher.Tests/) | Update and content logic with fake HTTP responses and temporary folders |
| [Release tools](../tools/release/) | Build packaging and launcher publication |

Unit tests do not cover a complete fresh installation or Windows SmartScreen behavior. When changing
installation or updating, also exercise a fresh install, an existing install, cancellation, offline play
and launcher self-update against the intended release assets.
