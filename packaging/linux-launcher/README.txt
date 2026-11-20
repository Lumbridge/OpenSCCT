OpenSCCT native Linux launcher (x86_64)

Run ./OpenSCCT.Launcher-linux-x64 from this folder, or run ./install.sh
to add it to your application menu and handle openscct:// website Join links.
The installer uses your own account and needs no sudo. In Games, choose
PlayStation 2 for a local ISO and PCSX2, PC / Steam for an installed copy, or
OpenSCCT Classic for the native Linux game and content. OpenSCCT Unreal builds
currently target Windows. Mods shows the choices for the selected edition.

The .NET runtime is included. Classic runs natively. Playing Windows PC
executables directly uses Wine. Steam manages compatibility for its own launch.
An X11 desktop or XWayland is required. Native graphics and font libraries come
from your distribution; see the Linux guide for package names and troubleshooting:
https://github.com/OpenSCCT/OpenSCCT/blob/main/docs/LINUX.md

The optional desktop install puts the launcher in:
  ${XDG_DATA_HOME:-~/.local/share}/OpenSCCTLauncher/
Game builds and packs are separate in OpenSCCT/ under the same data directory.
Use a writable local filesystem so the launcher can install its own updates.

Both the standalone binary and archive have SHA-256 files on the release.
Check the archive before extracting it:
  sha256sum -c OpenSCCT.Launcher-linux-x64.tar.gz.sha256
