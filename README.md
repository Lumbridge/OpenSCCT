<img align="right" src="game/branding/icon.svg" width="88" alt="OpenSCCT logo">

# OpenSCCT

**Spies vs. mercs, rebuilt for modern PCs.** OpenSCCT is a community rewrite of
Splinter Cell: Chaos Theory Versus in Godot 4 and C#.

[![CI](https://github.com/Lumbridge/OpenSCCT/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Lumbridge/OpenSCCT/actions/workflows/ci.yml)
[![Linux export](https://github.com/Lumbridge/OpenSCCT/actions/workflows/linux-export.yml/badge.svg?branch=main)](https://github.com/Lumbridge/OpenSCCT/actions/workflows/linux-export.yml)
[![Latest release](https://img.shields.io/github/v/release/Lumbridge/OpenSCCT)](https://github.com/Lumbridge/OpenSCCT/releases/latest)
[![Last commit](https://img.shields.io/github/last-commit/Lumbridge/OpenSCCT/main)](https://github.com/Lumbridge/OpenSCCT/commits/main/)

[Download](https://github.com/Lumbridge/OpenSCCT/releases/latest) ·
[Player hub](https://master.lumbridge.co.uk) ·
[Documentation](docs/README.md) ·
[Contribute](CONTRIBUTING.md) ·
[Report a bug](https://github.com/Lumbridge/OpenSCCT/issues/new/choose)

## Play

1. Download **OpenSCCT.Launcher.exe** from the [latest release](https://github.com/Lumbridge/OpenSCCT/releases/latest).
2. Run it and follow the main button: **Install**, then **Download game files**.
3. Click **PLAY**. The launcher checks for updates each time it starts.

You can also choose your own Chaos Theory install in the launcher to prepare content from it.
See the [launcher guide](docs/LAUNCHER.md) for setup, storage and troubleshooting.

| Platform | Status |
|---|---|
| Windows 10/11, 64-bit | Main player build, with an automatic updater |
| Linux | Experimental native and Flatpak builds; [installation guide](docs/LINUX.md) |
| Steam Deck | Experimental; hardware validation is still pending |

## What you can do

- Play online, host a server or join over LAN.
- Practice with bots and training challenges.
- Use the player hub to find servers, view profiles, leaderboards and match history.
- Record and watch demos.
- Build and test the simulation without original game assets.

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
| Windows client | Godot boot, TestBox and TestRange simulation smoke tests |
| Linux export | Native export and Flatpak smoke tests when relevant files change |
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

Use the .NET SDK pinned in [global.json](global.json). From the repository root:

```powershell
dotnet build OpenSCCT.slnx
dotnet test --solution OpenSCCT.slnx --no-build --ignore-exit-code 8
```

The Godot client builds separately. Start with the [development guide](docs/DEVELOPMENT.md)
for tool setup, a content-free smoke test and the project layout.

Bug reports, testing, clearer docs and code changes all help. Read
[CONTRIBUTING.md](CONTRIBUTING.md) before sending a pull request. Original game files,
converted assets and decompiled code stay outside Git; see the
[provenance rules](re/PROVENANCE.md).

## Project information

OpenSCCT is a fan project, not affiliated with or endorsed by Ubisoft. Splinter Cell and
Chaos Theory are trademarks of Ubisoft Entertainment. This repository contains project
code, tools, original art and attributed third-party components.

[Service terms](docs/legal/TERMS.md) · [Privacy](docs/legal/PRIVACY.md)
