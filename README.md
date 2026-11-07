<img align="right" src="game/branding/icon.svg" width="88" alt="OpenSCCT logo">

# OpenSCCT<br clear="right">

**Spies vs. mercs, rebuilt for modern PCs.** OpenSCCT is a community rewrite of
Splinter Cell: Chaos Theory Versus in Godot 4 and C#. Play the asymmetric stealth
and combat of spies versus mercenaries, with online services, bots, replays and map-making tools.

[![CI](https://github.com/Lumbridge/OpenSCCT/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Lumbridge/OpenSCCT/actions/workflows/ci.yml)
[![Linux export](https://github.com/Lumbridge/OpenSCCT/actions/workflows/linux-export.yml/badge.svg?branch=main)](https://github.com/Lumbridge/OpenSCCT/actions/workflows/linux-export.yml)
[![Latest release](https://img.shields.io/github/v/release/Lumbridge/OpenSCCT)](https://github.com/Lumbridge/OpenSCCT/releases/latest)
[![Last commit](https://img.shields.io/github/last-commit/Lumbridge/OpenSCCT/main)](https://github.com/Lumbridge/OpenSCCT/commits/main/)

[Download](https://github.com/Lumbridge/OpenSCCT/releases/latest) ·
[Player hub](https://master.lumbridge.co.uk) ·
[Map editor](https://github.com/Lumbridge/OpenSCCT-Editor) ·
[Documentation](docs/README.md) ·
[Contribute](CONTRIBUTING.md) ·
[Report a bug](https://github.com/Lumbridge/OpenSCCT/issues/new/choose)

## Play

On **Windows**:

1. Download **OpenSCCT.Launcher.exe** from the [latest release](https://github.com/Lumbridge/OpenSCCT/releases/latest) into a writable folder.
2. Run it and press **Install**. The launcher downloads OpenSCCT and its game files.
3. Click **PLAY**. By default, the launcher checks for updates each time it starts.

No original Chaos Theory installation is needed to play. The launcher downloads the required
shared content and stock maps; **Maps** manages additional downloads. See the
[launcher guide](docs/LAUNCHER.md) for setup, storage and troubleshooting.

| Platform | Status |
|---|---|
| Windows 10/11, 64-bit | Main player build, with an automatic updater |
| Linux, x86-64 | Native launcher with automatic updates; gameplay and alternative Flatpak builds remain experimental. [Installation guide](docs/LINUX.md) |
| Steam Deck | Experimental; hardware validation is still pending |

On **Linux**, download **OpenSCCT.Launcher-linux-x64** from a release with Linux assets,
make it executable and run it. The launcher archive also includes an installer for application-menu
and website Join links. See the [Linux guide](docs/LINUX.md) for dependencies and Flatpak setup.

## What you can do

| Feature | Details |
|---|---|
| Spies vs. mercenaries | Play **Story**, **Disk Hunt**, **Deathmatch** and **Merc Deathmatch**, with additional objective modes on custom maps that support them. [Game modes](docs/MODES.md) |
| Online and LAN | Browse servers, create a session or run a dedicated server. Configure lobby rules, loadouts and map rotations. [Joining and hosting](docs/ONLINE.md) |
| Solo and practice | Fill slots with bots, explore through **Visit Map**, or use practice commands to spawn, place and control bots. [Bots and practice](docs/AI.md), [tutorials and controls](docs/MENUS.md) |
| Social hub | Meet other players, use proximity voice, queue at match pads, or try the assault course and aim range. [In-game hub](docs/HUB.md), [voice chat](docs/VOICE.md) |
| Player progression | Track career stats, ranks, XP, achievements and cosmetic unlocks. Eligible rounds on official servers count toward progression. [Player stats](docs/PLAYER-STATS.md) |
| Replays and match history | Save demos, watch from player or free-camera views, and revisit permanent scoreboards. Playback needs a compatible build and matching map content. [Demos](docs/DEMOS.md), [scoreboards](docs/SCOREBOARD.md) |
| Player website | Find servers, join through the launcher, view profiles and leaderboards, and manage account privacy. [Player hub](https://master.lumbridge.co.uk), [guide](docs/PLAYER-HUB.md) |
| Map editor | Make and edit maps with [OpenSCCT Editor](https://github.com/Lumbridge/OpenSCCT-Editor), available as an optional install from the Windows launcher's **Editor** tab. |

Spies can also switch between third- and first-person views; see [menus and controls](docs/MENUS.md).

OpenSCCT is in active development. Matching the original game's behavior is ongoing;
automated checks do not cover every visual, audio or multiplayer interaction.
Follow [release notes](https://github.com/Lumbridge/OpenSCCT/releases) for shipped changes
and [open issues](https://github.com/Lumbridge/OpenSCCT/issues) for reported problems.

## CI and coverage

The CI badges above show the latest results on `main`. Pull requests run the same core CI:

| Check | What it covers |
|---|---|
| Linux and Windows | .NET builds and tests, plus checks that game data stays out of Git |
| Linux tools | Python tests for content conversion and reverse-engineering tools; dedicated-server publish |
| Windows client | Godot boot, TestBox, TestRange simulation and Help & feedback smoke tests |
| Linux export | Native launcher packaging and distribution smokes, native game export and Flatpak smokes when relevant files change |
| Documentation | Local links, heading anchors and the README coverage table |

See [testing and CI](docs/TESTING.md) for commands, artifacts and the limits of each check.

### Porting coverage

This measures how much of the original game's inventory has a specification, a marked C# port,
or a passing comparison scenario. **It is not line or branch test coverage, or a percentage of
the finished game.** Rebuilt systems can work without matching an original inventory unit.

<!-- coverage:start -->
Snapshot: **2026-10-28**. Generated from the checked-in coverage report.

| Scope | In-scope units | Specced | Ported | Verified | Ported bytecode |
|---|---:|---:|---:|---:|---:|
| All inventoried units | 7,084 | 4,282 (60.4%) | 3,414 (48.2%) | 168 (2.4%) | 53.3% |
| SBase gameplay package | 3,637 | 3,365 (92.5%) | 2,888 (79.4%) | 168 (4.6%) | 92.4% |

Bytecode percentages weight each unit by its original script size; units without
bytecode count only in the unit columns.
<!-- coverage:end -->

“Specced” includes ported and verified units; “ported” includes verified units. Percentages
exclude units marked out of scope. A marked port may still need gameplay checks. “Verified”
records a port with passing comparison results, or an explicit evidence-backed status.
See the [full report](docs/progress/COVERAGE.md), [history](docs/progress/history.csv)
and [measurement rules](tools/OpenScct.Coverage/README.md).

## Build and contribute

The simulation, dedicated server and tools build without original game assets.
Use the .NET SDK pinned in [global.json](global.json). From the repository root:

```powershell
dotnet build OpenSCCT.slnx
dotnet test --solution OpenSCCT.slnx --no-build --ignore-exit-code 8
```

The Godot client builds separately with the .NET editor pinned in [godot-version.txt](godot-version.txt).
On Windows:

```powershell
$godotExe = & ./tools/Get-Godot.ps1
dotnet build game/OpenScctGame.slnx
& $godotExe --editor --path game
```

Start with the [development guide](docs/DEVELOPMENT.md) for tool setup, content preparation,
a content-free smoke test and the project layout. The [Linux guide](docs/LINUX.md) covers
its build and packaging workflow; the [architecture guide](docs/ARCHITECTURE.md) explains the codebase.

Bug reports, testing, clearer docs and code changes all help. Read
[CONTRIBUTING.md](CONTRIBUTING.md) before sending a pull request. Original game files,
converted assets and decompiled code stay outside Git; see the
[provenance rules](re/PROVENANCE.md).

## Project information

OpenSCCT is a fan project, not affiliated with or endorsed by Ubisoft. Splinter Cell and
Chaos Theory are trademarks of Ubisoft Entertainment. This repository contains project
code, tools, original art and attributed third-party components.

[Service terms](docs/legal/TERMS.md) · [Privacy](docs/legal/PRIVACY.md)
