# OpenSCCT

An open rewrite of **Splinter Cell: Chaos Theory Versus** (spies vs. mercs) in Godot 4 and C#.
It plays like the original, runs on modern Windows, and updates itself.

You do not need your own copy of Splinter Cell: Chaos Theory: the launcher downloads the game files it needs. If you
have the game, you can point the launcher at it to convert the maps, characters and sounds from your copy instead.

## Linux and Steam Deck (experimental)

A native Linux export and Flatpak are available; see [Linux instructions](docs/LINUX.md) for building,
installation and first-run content downloads. Steam Deck hardware validation is pending.

## Get playing

1. **Download the launcher**: `OpenSCCT.Launcher.exe` from the
   [latest release](https://github.com/Lumbridge/OpenSCCT/releases/latest). Put it in any folder and run it.
   Windows may show a SmartScreen warning because the exe is not signed: click **More info**, then **Run anyway**.
2. **Follow the steps in the launcher.** Step 1 (your own game folder) is optional. The big green button always does
   the next step: Install, then Download game files (or Prepare game files, when you chose your own copy).
3. **Click PLAY.**

That's it. The launcher checks for updates each time it starts, downloads only what changed, and updates itself.

## If something goes wrong

- **Using your own copy**: click **Choose folder...** in step 1 and pick the folder you installed it in (its `System`
  folder works too). Making the game files from it is optional: PLAY keeps working with the files you already have.
- **A map or menu looks wrong after an update**: click **Rebuild** in step 3.
- **The new version misbehaves**: **Roll back** returns to the previous one.
- **Text language**: Settings has a Language list (English, French, German, Spanish, Italian, if your copy has it).

Everything the launcher writes is in `%LOCALAPPDATA%\OpenSCCT`. Deleting that folder resets it.

## Requirements

- Windows 10 or 11, 64-bit
- Optional: Splinter Cell: Chaos Theory (Steam, or a disc or Ubisoft install with its `Versus` folder)
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
