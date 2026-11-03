# Launcher

[Documentation](README.md)

The Windows launcher installs, updates and starts OpenSCCT. It downloads releases from
[Lumbridge/OpenSCCT](https://github.com/Lumbridge/OpenSCCT/releases/latest) and updates itself.
For Linux and Steam Deck, use the [Flatpak guide](LINUX.md).

## Install and play

1. Download **OpenSCCT.Launcher.exe** from the latest release and put it in a writable folder.
2. Run it and press **Install**.
3. Press **Download game files**, then **PLAY**.

Your own Chaos Theory install is optional. To use it, select **Choose folder** in step 1 of the launcher
and select the game folder, its `Versus` folder, or a folder inside `Versus`. The launcher then offers
**Prepare game files** to convert its maps, characters, sounds and menus locally.

The main button shows the next action. **Stop** or **Cancel** interrupts the current task;
completed work is kept where it can be reused. The launcher does not change the original install.

## Updates and troubleshooting

- **Update** installs a newer game build. **Play current version** lets you defer it for that launch.
  Play checks again for updates with a five-second timeout; a failed check still permits installed play.
- **Rebuild** prepares content again if an update leaves a map or menu looking wrong.
- **Maps** downloads additional converted maps, ready to play without conversion.
- **Website** opens the player hub. **What's new** opens its changelog, with built-in release notes if the
  website or browser is unavailable.
- **Language** in Settings selects English, French, German, Spanish or Italian when your original install
  contains that language. Downloaded packs and OpenSCCT's own menus are English.
- The launcher is unsigned, so Windows may show a SmartScreen warning on first launch.

Settings also includes prerelease updates, startup update checks, the release repository and shortcuts
to the launcher and game data folders. Public releases require no GitHub token.

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
| `launcher.json` | Launcher settings and optional original-install path |
| `versions/<version>/` | One exported game build per version |
| `downloads/` | Completed and partial release downloads |
| `cache/` | Cached release list and rate-limit state |
| `content/` | Locally converted content, build cache and logs |
| `packs/` | Downloaded [content packs](CONTENT-PACKS.md) |
| `maps/` | Original-format maps downloaded by older launchers |
| `token.dat` | Optional private-repository token, encrypted for the Windows user |

Game settings, bindings and saves live separately in
`%APPDATA%\Godot\app_userdata\OpenSCCT` (Godot's `user://`). The launcher does not delete that folder.
Deleting the launcher data folder removes installed builds and content; use it only when you intend to reinstall.

## Game content

Without an original install, the launcher downloads the required shared packs and maps from
[OpenSCCT-Content](https://github.com/Lumbridge/OpenSCCT-Content). See [Content packs](CONTENT-PACKS.md)
for their format, checksums and installation paths.

With an install selected, the bundled builder converts content into `content/`. Its cache reuses finished
steps after cancellation and rebuilds only changed inputs. Progress appears in the launcher; full output
is in `content/build.log`. `content/content-build.json` records the game version, source install, language
and outcome. At least one successful build is needed before playing with that converted content.

Locally converted maps and shared content take precedence over downloaded packs. Changing the language
requires another build. Missing translations fall back to English; map-localized properties and briefings
follow the original game's lookup rules.

The **Maps** window now uses converted packs. Original-format maps downloaded by older launchers remain
supported as an addon layer below the player's install. The install's files win when both contain the same
path. `ContentBuildPlan` passes that layer to the builder with `--addons`; removing an old download may
require content preparation again. The player's original game folder is never modified.

### Bundled converter

[Build-ContentTools.ps1](../tools/release/Build-ContentTools.ps1) packages CPython 3.10, the dependencies in
`content/requirements.txt`, and the tracked converter sources under `contenttools/`. It does not package
game-derived class defaults; the builder reads those from the player's install.

The launcher runs the equivalent of:

```text
contenttools\python\python.exe -u -m svmbuild classes chars audio ui maps-catalog renderpack fx maps audio-map --maps all --game "<install>" --out "<content>" --jobs <workers> --report "<content>\build-report.json"
```

Workers default to half the CPU cores, capped at six. Play passes `--svm-content=<content>` and, when an
install is selected, `--svm-game-root=<install>` / `SVM_GAME_ROOT`. Download-only play lets the game find
the installed packs. [Content tools](../content/README.md) covers checkout builds.

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

### Self-update and private forks

A newer launcher is downloaded, verified and staged beside the running executable as `.new`.
**Restart to finish** swaps it into place and removes the old copy after restart. The launcher recovers
an interrupted swap. A read-only folder prevents self-update; it does not trigger an elevation request.

Private forks may use a fine-grained GitHub token with read-only **Contents** access to that repository.
The token is encrypted with Windows DPAPI for the current user, never displayed again, and can be removed
in Settings. A token that cannot be decrypted is treated as absent.

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
