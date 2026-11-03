# Launcher

[Documentation](README.md)

The Windows launcher installs, updates and starts OpenSCCT. It downloads releases from
[Lumbridge/OpenSCCT](https://github.com/Lumbridge/OpenSCCT/releases/latest) and updates itself.
For Linux and Steam Deck, use the [Flatpak guide](LINUX.md).

## Install and play

1. Download **OpenSCCT.Launcher.exe** from the latest release and put it in a writable folder.
2. Run it and press **Install**. The launcher downloads OpenSCCT and its game files.
3. Press **PLAY** when both steps are ready.

The main button shows the next action. **Stop** or **Cancel** interrupts the current task;
completed downloads are kept so setup can resume. No original Chaos Theory install or folder selection is needed.

## Updates and troubleshooting

- **Update** installs a newer game build and any newer downloaded game files, including file-only updates.
  **Play current version** lets you defer updates for that launch.
  Play checks again for updates with a five-second timeout; a failed check still permits installed play.
- **Maps** downloads additional converted maps, ready to play without conversion.
- **Website** opens the player hub. **What's new** opens its changelog, with built-in release notes if the
  website or browser is unavailable.
- Downloaded game files and menus are in English.
- The launcher is unsigned, so Windows may show a SmartScreen warning on first launch.

Settings includes startup update checks, optional preview releases, folder shortcuts and installation reset.
Save and Cancel stay visible while the settings scroll on smaller screens.
The launcher gets updates from the public `Lumbridge/OpenSCCT` repository without a GitHub account or token.

## Help and contributions

Choose **Help** for documentation, bug reports, suggestions, existing issues and contribution guides.
The same window links to developer setup and pull requests. Help stays available while the launcher
downloads or prepares files, and opening it preserves the current progress or error message.

**Report a bug** opens the [website bug report form](https://master.lumbridge.co.uk/help/report-bug).
Sign in with Discord, review the report and submit it there. The form is prefilled for the launcher
with its version, the installed game version, build information and operating system. **Suggest a feature**
and pull requests use GitHub and require GitHub sign-in.

**Copy details** copies the same version information. The launcher does not attach logs, account details
or local file paths. If the browser cannot open, use **Copy link** or select the address and copy it
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

The launcher stores its files under `%LOCALAPPDATA%\OpenSCCT`:

| Path | Contents |
| --- | --- |
| `install.json` | Current, previous and pending game versions |
| `launcher.json` | Startup update and preview-release preferences |
| `versions/<version>/` | One exported game build per version |
| `downloads/` | Completed and partial release downloads |
| `cache/` | Cached release list and rate-limit state |
| `content/` | Legacy converted content and logs, no longer used by the launcher |
| `packs/` | Downloaded [content packs](CONTENT-PACKS.md) |
| `maps/` | Original-format maps downloaded by older launchers |

Game settings, bindings and saves live separately in
`%APPDATA%\Godot\app_userdata\OpenSCCT` (Godot's `user://`). The launcher does not delete that folder.
Deleting the launcher data folder removes installed builds and content; use it only when you intend to reinstall.

## Game content

The launcher downloads the required shared packs and maps from
[OpenSCCT-Content](https://github.com/Lumbridge/OpenSCCT-Content). See [Content packs](CONTENT-PACKS.md)
for their format, checksums and installation paths.

The first setup downloads the shared packs and stock maps. **Maps** downloads, updates and removes
additional ready-to-play map packs. Missing maps can also be downloaded in game when joining a lobby.

Existing installations that used local conversion now download the required packs before playing.
The launcher starts the game using its downloaded packs, without legacy content overrides or inherited
original-install paths. Existing game settings, bindings and saves
are kept. Legacy converted files remain on disk until an installation reset or manual cleanup.

Local conversion remains a developer workflow; see [Content tools](../content/README.md).

## Build and publish

```powershell
dotnet build OpenSCCT.slnx
dotnet test --project tests/OpenScct.Launcher.Tests
dotnet publish src/OpenScct.Launcher -c Release -r win-x64
```

The publish command creates a self-contained `OpenSCCT.Launcher.exe` in
`src/OpenScct.Launcher/bin/Release/net10.0-windows/win-x64/publish/`. Players do not need .NET installed.
For a framework-dependent build, add `--self-contained false -p:PublishReadyToRun=false`; that build
requires the .NET 10 Desktop Runtime.

### Version and build number

[Directory.Build.props](../Directory.Build.props) defines `VersionPrefix` for the game and
`LauncherVersion` for the launcher. [Directory.Build.targets](../Directory.Build.targets) derives the
build number from the commit's UTC timestamp (`yyyyMMdd.HHmm`).

Bump `LauncherVersion` when launcher code, its shared dependencies or branding change. Game releases
reuse the previously built launcher when its version is unchanged. Packaging refuses changed launcher
sources under an already-published launcher version.

### Making a release

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
| `OpenSCCT-<version>-win-x64-pack.zip` | Smaller new files, with entries named by hash |
| `<asset>.sha256` | Checksums for the file list and launcher |

The updater reuses unchanged files from the current and previous installations, then downloads and
verifies missing files. Downloads resume where supported. It installs into a staging directory and
switches `install.json` atomically. A running game keeps its version; the switch waits until it exits.
Cleanup retains the current and previous builds and avoids paths used by running games.

**Keep the `game-files` release and every asset referenced by a published file list.** Older game
versions can depend on those files. The publishing script stops before the release reaches 990 assets.
`-BaseTag` selects a different delta base; `-NoDelta` creates a complete set of new blobs.

When that store is nearly full, use `New-Release.ps1 -StoreTag game-files-2` (or another new tag).
New blobs go to the new storage prerelease; reused blobs keep their original release tags.
The version release also carries the file list, its checksum and the launcher's checksum, which existing
launchers discover before looking in `game-files`. Delta lookup falls back to `game-files` for older bases.
Keep every earlier storage release and its referenced assets.

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
| [OpenScct.Launcher.Core](../src/OpenScct.Launcher.Core/) | Release selection, downloads, checksums, installation and launch |
| [OpenScct.Maps](../src/OpenScct.Maps/) | Shared map catalogue, pack downloads and content-building support |
| [Launcher tests](../tests/OpenScct.Launcher.Tests/) | Update and content logic with fake HTTP responses and temporary folders |
| [Release tools](../tools/release/) | Build packaging and launcher publication |

Unit tests do not cover a complete fresh installation or Windows SmartScreen behavior. When changing
installation or updating, also exercise a fresh install, an existing install, cancellation, offline play
and launcher self-update against the intended release assets.
