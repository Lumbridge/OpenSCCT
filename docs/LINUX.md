# Native Linux and Steam Deck (experimental)

The Linux preset exports the Godot C# game as a native x86_64 executable with a self-contained .NET runtime.
The Flatpak starts the game directly and provides first-run content setup in place of the Windows Forms launcher.
Steam Deck hardware validation is pending; desktop Guix x86_64 has been tested with the certificate-path fix.

## Install

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

On first launch select **Download and play**. The starter download fetches the shared packs and Steel Squat
(`SteeE`), approximately 490 MiB compressed. Allow about 2 GiB free for extraction and installation. All stock
maps are optional; more maps are available later under **Content**. Archives use the existing size/SHA-256 and
map-manifest verification. Completed packs survive cancellation; an interrupted archive restarts on retry.
Downloaded game content is not bundled in the Flatpak or committed to the repository.

If setup reports an incomplete selected content folder, correct the `--svm-content` argument or `SVM_CONTENT`
environment variable. Remove both overrides to use downloaded packs. Setup does not modify a supplied converted
content folder, and downloading packs elsewhere cannot repair it.

Use **Create a Session** and **Bots: Fill Slots** for a solo match. For local play, turn public listing and UPnP
off and leave the master URL empty. After downloading content, direct offline practice also works:

```sh
flatpak run io.github.Lumbridge.OpenSCCT -- --svm-play=SteeE --bots=3
```

## Steam and controllers

In Desktop Mode, add OpenSCCT from the application menu as a non-Steam game, then launch it from Gaming Mode.
For a manual Steam entry, use `/usr/bin/flatpak` as the executable and `run io.github.Lumbridge.OpenSCCT` as
launch options. Leave compatibility-tool overrides off and choose a **Gamepad** Steam Input layout. The game
already supports analog sticks, menu navigation and separate spy/mercenary bindings. Steam+X opens the keyboard.

Rendering uses upstream's Forward+ Vulkan path. For graphics troubleshooting, try
`flatpak run io.github.Lumbridge.OpenSCCT --rendering-method gl_compatibility`; some effects require Forward+.
Hardware frame rate, Gaming Mode integration, controller mapping and microphone capture need device testing.

## Data and certificates

Application ID: `io.github.Lumbridge.OpenSCCT`.

- Packs: `~/.var/app/io.github.Lumbridge.OpenSCCT/data/OpenSCCT/packs/`
- Settings/crash logs: `~/.var/app/io.github.Lumbridge.OpenSCCT/data/godot/app_userdata/OpenSCCT/`
- Terminal output: launch using `flatpak run io.github.Lumbridge.OpenSCCT`.

The launcher replaces missing/unreadable inherited `SSL_CERT_FILE` and `SSL_CERT_DIR` values with the runtime's
`/etc/ssl/cert.pem` and `/etc/ssl/certs`. This fixes Guix/Nix profile paths that do not exist inside the sandbox.
Readable custom CA files/directories, including colon-separated directory lists, are preserved. Certificate
verification remains enabled. The launcher reports replacement paths on stderr.

The sandbox allows network, IPC, X11/Wayland, audio/microphone and devices (GPU/controllers), with no home-directory
access. The Windows-only Python content converter is omitted; this package uses upstream preconverted packs.
The Windows launcher's self-update mechanism is not used. Discord remains disabled on Linux as upstream configures it.

## Build

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

Outputs: `local/linux/`, `local/dist/OpenSCCT-linux-x86_64.flatpak` and `local/dist/SHA256SUMS`.
To package an existing export: `tools/linux/package-flatpak.sh local/linux`. Builds default to two .NET processors
and sequential project compilation. Packaging uses `flatpak build-export --disable-sandbox` for the icon validator
so it can run in nested containers; this does not weaken the installed application's sandbox.

The `linux-export` GitHub Actions workflow builds the bundle, checks the native boot/TestBox smokes and uploads
an `openscct-linux-flatpak` artifact. Its ordinary run downloads toolchains, not game content.

Third-party notices for the engine, runtime, fonts and dependencies ship with the export and Flatpak.
Application source licensing is unchanged from upstream.

## Validate

`tools/linux/smoke.sh` runs boot and TestBox checks against the exported executable, covering the bundled runtime,
simulation and loopback networking. `OPENSCCT_NETWORK_TEST=1 tools/linux/smoke.sh` additionally runs `linux-setup`
with separate XDG directories. That opt-in test fetches real content and requires shared packs, SteeE and a
transition to the main menu. Its timeout is 600 seconds; it is excluded from the ordinary smoke-suite list.

To validate an installed bundle, run the same smoke flags through `flatpak run io.github.Lumbridge.OpenSCCT`.
Some nested containers block Flatpak's user namespaces; testing native files there does not validate a complete
Flatpak sandbox launch or Steam Deck hardware behavior.
