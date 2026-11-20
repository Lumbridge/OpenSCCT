OpenSCCT native Linux launcher (x86_64)

Run ./OpenSCCT.Launcher-linux-x64 from this folder, or run ./install.sh
to add it to your application menu and handle openscct:// website Join links.
The installer uses your own account and needs no sudo. Under Originals, choose
PlayStation 2 for a local ISO or PC / Steam for an installed copy. Under
Modernisations, choose OpenSCCT Godot to build the native game's assets and
original Versus maps from the installed Steam game. OpenSCCT Unreal builds
currently target Windows. Mods shows the choices for the selected edition.

The .NET runtime is included. Godot runs natively. Playing Windows PC
executables directly uses Wine. Steam manages compatibility for its own launch.
An X11 desktop or XWayland is required. Native graphics and font libraries come
from your distribution; see the Linux guide for package names and troubleshooting:
https://github.com/OpenSCCT/OpenSCCT/blob/main/docs/LINUX.md

The optional desktop install puts the launcher in:
  ${XDG_DATA_HOME:-~/.local/share}/OpenSCCTLauncher/
Game builds, converted content and additional maps are separate in OpenSCCT/ under the same data directory.
Use a writable local filesystem so the launcher can install its own updates.

Both the standalone binary and archive have SHA-256 files on the release.
Check the archive before extracting it:
  sha256sum -c OpenSCCT.Launcher-linux-x64.tar.gz.sha256
