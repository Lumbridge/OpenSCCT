# OpenSCCT

An open rewrite of **Splinter Cell: Chaos Theory Versus** (spies vs. mercs) in Godot 4 and C#.
It plays like the original, runs on modern Windows, and updates itself.

You need your own copy of Splinter Cell: Chaos Theory. OpenSCCT does not include any of the game's files: the
launcher converts the maps, characters and sounds from your install on your own PC.

## Get playing

1. **Install Splinter Cell: Chaos Theory** (the Steam version works; it is found automatically).
2. **Download the launcher**: `OpenSCCT.Launcher-<version>-win-x64.exe` from the
   [latest release](https://github.com/Lumbridge/OpenSCCT/releases/latest). Put it in any folder and run it.
   Windows may show a SmartScreen warning because the exe is not signed: click **More info**, then **Run anyway**.
3. **Follow the three steps in the launcher.** It finds your game, downloads OpenSCCT and prepares the game files
   from your copy. The big green button always does the next step (Install, then Prepare game files); the first
   time takes a few minutes.
4. **Click PLAY.**

That's it. The launcher checks for updates each time it starts, downloads only what changed, and updates itself.

## If something goes wrong

- **Step 1 says the game was not found**: click **Choose folder...** and pick the folder you installed it in.
- **A map or menu looks wrong after an update**: click **Rebuild** in step 3.
- **The new version misbehaves**: **Roll back** returns to the previous one.
- **Text language**: Settings has a Language list (English, French, German, Spanish, Italian, if your copy has it).

Everything the launcher writes is in `%LOCALAPPDATA%\OpenSCCT`. Deleting that folder resets it.

## Requirements

- Windows 10 or 11, 64-bit
- Splinter Cell: Chaos Theory (Steam, or a disc or Ubisoft install with its `Versus` folder)
- About 1.5 GB of disk space

## For developers

```powershell
dotnet build OpenSCCT.slnx              # the engine, server, launcher and tools
dotnet build game/OpenScctGame.slnx     # the Godot game project (Godot 4.7.2 .NET)
```

Building content in a checkout needs your own install too; see `docs/LAUNCHER.md` and `docs/ARCHITECTURE.md`.
The rules for what may be committed (no game files, no game text, no decompiled code) are in `re/PROVENANCE.md`.

## Legal

OpenSCCT is a fan project, not affiliated with or endorsed by Ubisoft. Splinter Cell and Chaos Theory are
trademarks of Ubisoft Entertainment. This repository holds only our own code, tools and art.
