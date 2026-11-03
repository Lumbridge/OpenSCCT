# Documentation

Start with the guide for your task. The [project README](../README.md) has downloads and a
short overview; detailed behavior and evidence live in the [specifications](../spec/README.md).

## Play and host

| I want to… | Read |
|---|---|
| Install, update or troubleshoot Windows | [Launcher](LAUNCHER.md) |
| Play on Linux or Steam Deck | [Linux](LINUX.md) |
| Join friends or host a server | [Online play and hosting](ONLINE.md) |
| Use the website, account or privacy settings | [Player hub](PLAYER-HUB.md) |
| Understand stats and progression | [Player stats](PLAYER-STATS.md) |
| Use the in-game hub | [Hub](HUB.md) |
| Practice or play with bots | [Training](TRAINING.md), [AI bots](AI.md) |
| Choose a mode | [Game modes](MODES.md) |
| Record or watch a match | [Demos](DEMOS.md) |
| Set up voice or Discord features | [Voice](VOICE.md), [Discord](DISCORD.md) |
| Manage downloaded maps and shared content | [Content packs](CONTENT-PACKS.md) |

## Develop and test

| I want to… | Read |
|---|---|
| Build from source | [Development setup](DEVELOPMENT.md) |
| Send a fix or improve a guide | [Contributing](../CONTRIBUTING.md), [conventions](CONVENTIONS.md) |
| Understand the codebase | [Architecture](ARCHITECTURE.md) |
| Run tests and understand CI | [Testing and CI](TESTING.md) |
| Test the player input and presentation path | [Play testing](TESTING-PLAY.md) |
| Use synthetic maps | [TestRange](TESTMAP.md) |
| Compare behavior with the original | [Scenario format](../tests/scenarios/README.md), [comparison tools](../re/tools/difftest/README.md) |
| Read porting metrics | [Coverage report](progress/COVERAGE.md), [measurement rules](../tools/OpenScct.Coverage/README.md) |

## Technical reference

These guides describe implementation details. Player setup instructions are above.

| Area | Guides |
|---|---|
| Simulation | [Physics](PHYSICS.md), [gadgets](GADGETS.md), [AI](AI.md) |
| Presentation | [Rendering](RENDERING.md), [animation](ANIMATION.md), [effects](EFFECTS.md), [audio](AUDIO.md), [fitted skins](FITTED-SKINS.md) |
| Interface | [Menus](MENUS.md), [HUD](HUD.md), [player stats](PLAYER-STATS.md) |
| Networking | [Netcode](NETCODE.md), [parity notes](NETCODE-PARITY.md), [network lab](NETWORK-LAB.md), [two-PC testing](NETWORK-TEST.md) |
| Content and formats | [Maps](MAPS.md), [content pipeline](../content/README.md), [SVMap 1.1](../spec/svmap/1.1/README.md) |
| Code generation | [Generated defaults and dispatch](CODEGEN.md) |
| Original-game research | [Research tools](../re/README.md), [provenance](../re/PROVENANCE.md), [specifications](../spec/README.md) |
| Deployment | [Online services](ONLINE.md), [US lobby templates](../tools/deploy/us-lobbies/README.md) |

## Reports and research history

These are records of a particular investigation or test run. Their results and open items
may have changed. Use the current guides and source when making a new change.

- [Research spikes](spikes/README.md): early decisions and experiments.
- [Gap analysis, 28 October 2026](progress/GAPS.md).
- [Initial networking milestone](NETCODE-M0.md) and [network improvement plan](NETWORK-IMPROVEMENT-PLAN.md).
- [Network overnight run](NETWORK-OVERNIGHT-REPORT-2026-11-01.md),
  [follow-up](NETWORK-FOLLOWUP-2026-11-01.md) and [presentation findings](NETWORK-PRESENTATION-2026-11-01.md).
- [Performance investigation](PERFORMANCE-INVESTIGATION-2026-11-01.md),
  [overnight run](PERFORMANCE-OVERNIGHT-REPORT-2026-11-01.md) and
  [clean rerun](PERFORMANCE-CLEAN-RERUN-2026-11-01.md).
- [Map parity audit](MAP-PARITY-2026-11-01.md) and
  [benchmark results](../tools/benchmark/RESULTS-2026-10-31.md).

[Service terms](legal/TERMS.md) · [Privacy](legal/PRIVACY.md)
