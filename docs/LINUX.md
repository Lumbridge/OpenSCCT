# Native Linux launcher and Steam Deck

[Documentation](README.md)

The native x86-64 launcher manages your Chaos Theory editions and installs OpenSCCT Godot on Linux.
The launcher builds the Godot game's assets and original Versus maps from your installed Steam copy of Chaos Theory.
It includes its own .NET runtime and supports optional map downloads, offline play, delta updates and launcher self-update. See [Launcher](LAUNCHER.md).

Use a [release](https://github.com/OpenSCCT/OpenSCCT/releases) containing
`OpenSCCT.Launcher-linux-x64` and a matching Linux game file list. Older Windows-only releases do not
contain these assets. A source build or CI artifact does not mean a Linux release has been published.

The alternative Flatpak starts the game directly and reads the content built by the native launcher. Its
application updates require installing another bundle; the native launcher manages game updates itself.

Linux gameplay and Steam Deck support remain experimental. Steam Deck hardware validation is still pending.
For build details and automated checks, see [Build](#build) and [Validate](#validate).

## Native launcher

The portable launcher targets current **Arch Linux**, **Ubuntu 22.04 or newer**, **Debian 12 or newer**,
and **Fedora** on x86-64 desktops. It uses glibc, X11/XWayland and the distribution's graphics drivers.
ARM, Alpine/musl and a Wayland-only session without XWayland are not supported by this package.

Save `OpenSCCT.Launcher-linux-x64` in a writable folder, then run:

```sh
chmod +x OpenSCCT.Launcher-linux-x64
./OpenSCCT.Launcher-linux-x64
```

For application-menu and website Join integration, use `OpenSCCT.Launcher-linux-x64.tar.gz` instead:

```sh
tar -xzf OpenSCCT.Launcher-linux-x64.tar.gz
cd OpenSCCT-Launcher
./install.sh
```

The installer copies the launcher to `${XDG_DATA_HOME:-~/.local/share}/OpenSCCTLauncher/`, adds a
desktop entry and registers `openscct://` links for the current user. It does not need `sudo`.
The archive contains dependency notices. Checksums accompany both the binary and archive in the release.

### Editions

Choose an edition in **Games**. **Originals** contains the PS2 and PC releases. **Modernisations** groups the Godot and Unreal versions, which bring new graphics and features to Versus. At smaller window widths, the edition selector moves above the page.
**Mods** follows the selected edition.

| Edition | Setup and play | Mods |
| --- | --- | --- |
| PlayStation 2 | Choose your original Chaos Theory ISO and an executable PCSX2 binary or AppImage. Set up your BIOS in PCSX2. **Play with PCSX2** starts the selected image. | Select a separately patched ISO for the same disc serial. **Use original disc** switches back. |
| PC / Steam | The launcher checks Steam libraries for Chaos Theory (app 13570). You can also select a stock game folder. Start campaign and co-op in Steam for a detected Steam installation, or through Wine for a manual copy. **Play Versus with Wine** starts the selected Versus executable. | Under **Mods**, choose a separately modded PC installation. A Versus-only copy such as Enhanced or Reloaded uses the stock campaign and co-op. A campaign-only copy uses stock Versus. Switch back to the stock installation at any time. |
| OpenSCCT Godot | Under **Modernisations**, select your Chaos Theory Steam installation if prompted. **Install** downloads the native client and builds the game assets and original Versus maps from Steam. Existing installs can use **Build from Steam**. **PLAY** and website Join links use the local build. | **Maps** and the **Mods** view offer community maps. Reviewed map archives install directly. Source mods download their original mod files, then build locally using your Steam assets. |
| OpenSCCT Unreal | Under **Modernisations**, the Linux launcher links to the Windows x64 releases. | Manage Unreal maps from the Windows launcher. |

The PC executable launch actions require a working `wine` command on your path. They inherit your Wine environment,
including `WINEPREFIX`. The Steam action opens the installed Steam client and uses its compatibility settings.
PCSX2 manages its own controller, graphics, memory cards and save states.

**Settings** controls Godot preview releases and startup update checks. **Play current version**
defers a Godot update, and installed play works when release checks are offline. **Stop** cancels
an operation while retaining completed downloads. A running Godot game keeps its version until it exits.

Launcher updates are verified and staged beside the executable. Choose **Restart to finish** to apply one.
Keep the executable in a user-writable directory so it can replace itself without root. Package-manager
installations under `/usr` generally cannot self-update and are not the distribution format provided here.

The launcher needs the distribution packages supplying `libX11`, `libICE`, `libSM`, `fontconfig`, OpenSSL 3,
the C/C++ runtimes, zlib and CA certificates. Wayland desktops also need XWayland. The game additionally
needs working Vulkan/OpenGL drivers and audio libraries. These are normally present on a full desktop;
minimal installations may need to install them. The launcher bundles .NET and Avalonia's native libraries.
On Fedora 45, install `openssl3-libs` (`sudo dnf install openssl3-libs`); its default OpenSSL 4 package
does not provide the OpenSSL 3 libraries used by the bundled .NET runtime.

Launcher data is `${XDG_DATA_HOME:-~/.local/share}/OpenSCCT/`, including `versions/`, `downloads/`,
`cache/`, `content/` (Steam builds) and `packs/` (downloaded maps). Game settings remain separately under
`${XDG_DATA_HOME:-~/.local/share}/godot/app_userdata/OpenSCCT/`. Resetting the launcher installation
does not delete game settings. Flatpak data lives under its own sandbox path and is not moved automatically.

## Flatpak install

In Desktop Mode on a Steam Deck, or a Linux desktop, open a terminal in the folder containing the bundle:

```sh
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user ./OpenSCCT-linux-x86_64.flatpak
flatpak run io.github.Lumbridge.OpenSCCT
```

The install pulls `org.freedesktop.Platform//25.08` and its graphics driver from Flathub if missing. Wine,
Proton and a system .NET installation are not required. This local bundle is unsigned and has no application
update remote: install a newer bundle to update it. If an older local installation reports a missing origin,
uninstall the application normally and reinstall the bundle; omit `--delete-data` to retain maps and settings.

Before starting the Flatpak, use the native launcher to build Godot content from your Steam installation.
The Flatpak reads the native launcher's current Steam build under
`${XDG_DATA_HOME:-~/.local/share}/OpenSCCT/content`. It also reads maps installed with the native launcher under
`OpenSCCT/packs/svmap`, including community mods built from Steam. Its own downloads and saves stay in the
Flatpak's own storage. An upgrade retains existing saves and previously downloaded optional maps.
If the game asks for Steam content, finish **Build from Steam** in the native launcher, then restart the Flatpak.

Use **Create a Session** and **Bots: Fill Slots** for a solo match. For local play, turn public listing and UPnP
off and leave the master URL empty. After building content, direct offline practice also works:

```sh
flatpak run io.github.Lumbridge.OpenSCCT -- --svm-play=AQU03 --bots=3
```

## Steam and controllers

In Desktop Mode, add OpenSCCT from the application menu as a non-Steam game, then launch it from Gaming Mode.
For a manual Steam entry, use `/usr/bin/flatpak` as the executable and `run io.github.Lumbridge.OpenSCCT` as
launch options. Leave compatibility-tool overrides off and choose a **Gamepad** Steam Input layout. The game
already supports analog sticks, menu navigation and separate spy/mercenary bindings. Steam+X opens the keyboard.

Started outside Steam (for example from the launcher in Desktop Mode), the Deck uses Steam's desktop layout, which
sends keys, clicks and mouse movement together with the controller. The game notices these copies after a few
presses, says so on screen and then ignores the keyboard and mouse in play while the controller is in use (they
work again three seconds after the last controller input, and always in menus and text fields). Starting the game
from Steam with a Gamepad layout avoids the copies altogether.

Rendering uses Godot's Forward+ Vulkan path. For graphics troubleshooting, try
`flatpak run io.github.Lumbridge.OpenSCCT --rendering-method gl_compatibility`; some effects require Forward+.
Hardware frame rate, Gaming Mode integration, controller mapping and microphone capture need device testing.

## Data and certificates

Application ID: `io.github.Lumbridge.OpenSCCT`.

- Packs: `~/.var/app/io.github.Lumbridge.OpenSCCT/data/OpenSCCT/packs/`
- Settings/crash logs: `~/.var/app/io.github.Lumbridge.OpenSCCT/data/godot/app_userdata/OpenSCCT/`
- Terminal output: launch using `flatpak run io.github.Lumbridge.OpenSCCT`.

The Flatpak startup script replaces missing or unreadable inherited `SSL_CERT_FILE` and `SSL_CERT_DIR` values with the runtime's
`/etc/ssl/cert.pem` and `/etc/ssl/certs`. This fixes Guix/Nix profile paths that do not exist inside the sandbox.
Readable custom CA files/directories, including colon-separated directory lists, are preserved. Certificate
verification remains enabled. The script reports replacement paths on stderr.

The sandbox allows network, IPC, X11/Wayland, audio/microphone and devices (GPU/controllers). Its host filesystem
grants provide read-only access to the native launcher's content and installed map folders.
The Flatpak does not use the native launcher's self-update mechanism. Discord Rich Presence is disabled on Linux.

## Build

With the repository's .NET SDK and Python 3 installed, build the launcher independently of the game:

```sh
bash tools/linux/build-launcher.sh
```

Outputs in `local/dist/`: `OpenSCCT.Launcher-linux-x64`, `OpenSCCT.Launcher-linux-x64.tar.gz`,
the archive checksum and a launcher-version-specific binary checksum. The launcher can also be
cross-published from Windows with `dotnet publish src/OpenScct.Launcher.Linux -c Release -r linux-x64`.

### Game and Flatpak

Install `flatpak`, `curl`, `unzip`, `python3`, `ripgrep` and Godot's Linux runtime dependencies. On Debian/Ubuntu
these include `libxcursor1`, `libxinerama1`, `libxi6`, `libxrandr2`, `libgl1`, `libasound2` (`libasound2t64` on
newer Ubuntu) and `libpulse0`. Run `tools/linux/bootstrap.sh` to fetch the pinned .NET SDK, Godot .NET editor and
Linux export templates into local/user directories. Godot downloads are checked against pinned upstream SHA-512
sums. Bootstrap downloads the full templates archive but extracts only Linux x86_64 templates.

```sh
export DOTNET_ROOT="$PWD/local/toolchain/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export GODOT="$PWD/local/toolchain/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64"
tools/linux/build.sh
```

Game outputs: `local/linux/`, `local/dist/OpenSCCT-linux-x86_64.flatpak` and `local/dist/SHA256SUMS`.
To package an existing export: `tools/linux/package-flatpak.sh local/linux`. Builds default to two .NET processors
and sequential project compilation. Packaging uses `flatpak build-export --disable-sandbox` for the icon validator
so it can run in nested containers; this does not weaken the installed application's sandbox.

The [linux-export workflow](../.github/workflows/linux-export.yml) builds the bundle, checks the native boot/TestBox smokes and uploads
an `openscct-linux-flatpak` artifact. Its ordinary run downloads toolchains, not game content.
It also installs the bundle and runs boot/TestBox inside the Flatpak sandbox using `tools/linux/smoke-flatpak.sh`.

Third-party notices for the engine, runtime, fonts and dependencies ship with the export and Flatpak.

### Prepare a release

After exporting the Linux game and building the launcher, run PowerShell 7 on Linux:

```sh
pwsh tools/release/New-Release.ps1 -Version <game-version> -Platform linux-x64 \
  -BuildDir local/linux -LauncherExe local/dist/OpenSCCT.Launcher-linux-x64
```

This prepares `local/release/<game-version>-linux-x64/`; it does not publish anything. Review its
`notes.md` and generated `publish.ps1`. Prepare the Windows assets separately using the existing command.
The generated scripts can attach both platforms to the same draft release. Publish the draft only after
both platforms are attached and checked. They refuse to replace assets on an already-published version.

Linux game updates use `OpenSCCT-<version>-linux-x64-files.json`, its SHA-256 file and the same shared blob
store/delta format as Windows. Manifests are also attached to the version release so Linux launchers can
skip newer Windows-only releases. Launcher-only releases use
`tools/release/Update-Launcher.ps1 -Platform linux-x64 -Notes "..."` on Linux; that command uploads immediately,
as described in [Launcher](LAUNCHER.md#making-a-release).

## Validate

The Linux workflow publishes the launcher on Ubuntu 22.04, runs its core tests, and exercises the portable
package in Ubuntu 22.04, Debian 12, Fedora and Arch containers. The package smoke installs into an isolated
user data folder, validates the desktop entry and starts the GUI under Xvfb. It makes no game-content downloads.

```sh
dotnet test --project tests/OpenScct.Launcher.Tests
python3 -m unittest discover -s tools/linux/tests -v
bash tools/linux/smoke-launcher.sh local/dist/OpenSCCT.Launcher-linux-x64.tar.gz
```

The package smoke requires `xvfb-run`, `xauth`, `desktop-file-validate` and the native GUI dependencies.
`OpenSCCT.Launcher-linux-x64 --smoke` exercises installation/update orchestration without a display or
network. `--smoke-ui` opens temporary test windows and exits; set `OPENSCCT_LAUNCHER_SMOKE_OUTPUT` to
save screenshots. These checks do not replace a complete graphical desktop/gameplay test with live releases.

`tools/linux/smoke.sh` checks boot, TestBox and help feedback against the exported executable. It also starts
`linux-setup` with an empty data directory to check the first-run instructions for building from Steam.
These tests cover the bundled runtime, simulation and loopback networking and can run offline.

To validate an installed bundle, run the same smoke flags through `flatpak run io.github.Lumbridge.OpenSCCT`.
Some nested containers block Flatpak's user namespaces; testing native files there does not validate a complete
Flatpak sandbox launch or Steam Deck hardware behavior.
