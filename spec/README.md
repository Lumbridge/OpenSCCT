# Behaviour specifications

These specs describe the original game's rules and the behaviour OpenSCCT aims to reproduce.
They also record deliberate additions and differences. Start with the topic below, then follow
its links to individual rules, source evidence and unresolved questions.

For build instructions and the current implementation, start with the
[developer guide](../docs/DEVELOPMENT.md) and [architecture](../docs/ARCHITECTURE.md).

## Find a topic

| Topic | What you will find |
|---|---|
| [Movement](movement/INDEX.md) | Walking, jumping, climbing, traversal and movement states |
| [Combat](combat/INDEX.md) | Weapons, damage, grabs, knockouts, death and healing |
| [Equipment](equipment/INDEX.md) | Gadgets, inventory, vision modes and weapon behaviour |
| [Objectives](objectives/INDEX.md) | Missions, hacking, disks, switches and team interactions |
| [Game flow](game/INDEX.md) | Match settings, teams, spawning, scoring and round changes |
| [Map devices](devices/INDEX.md) | Doors, lifts, triggers, alarms, cameras and other map actors |
| [HUD](hud/INDEX.md) | Messages, reticles, menus, scoreboard and strategic map |
| [Networking](net/INDEX.md) | Replication, movement messages, state changes and synchronization |
| [Native physics](native/physics-index.md) | Collision, physics modes and traversal queries |
| [Native networking](native/net-index.md) | Engine networking behind the script rules |
| [Native studies](native/) | Animation, input, camera, audio and other engine research |
| [Enhanced features](enhanced/FEATURES.md) | Enhanced-version behaviour and parity notes |
| [Reloaded features](reloaded/FEATURES.md) | Reloaded changes and evidence |
| [OpenSCCT additions](additions/) | Hub, progression, gear changes, merc sidearms, match-size rules, map outfits, conveyors, mover legs and the editor's test start |
| [SVMap 1.1](svmap/1.1/README.md) | Current map format; [1.2](svmap/1.2/README.md) adds map identity and pictures, match-size rules, outfits, conveyors and mover leg times, [1.0](svmap/1.0/README.md) documents the earlier format |

## Read the evidence

Specs are written in our own words. Their evidence usually points to decoded behaviour,
measurements from the original, or a test scenario. **Estimated** means the behaviour still
needs confirmation. An address or function name identifies evidence; it does not prove the
current port behaves correctly.

A spec can describe a feature that is incomplete in OpenSCCT. Read the linked code and tests
alongside the [coverage report](../docs/progress/COVERAGE.md) and
[known gaps](../docs/progress/GAPS.md). Older index counts and research notes are snapshots.
The generated coverage report is the source for published coverage figures.

## Edit a spec

- Keep rules short and concrete: say what triggers an action, what changes, and when it ends.
- Keep constants, units, state names and evidence precise. Mark guesses as estimated.
- Preserve existing headings when code links to them through provenance attributes.
- Preserve each `Units:` block. The coverage tool reads it up to the next blank line or heading.
- Put game-derived dumps, traces and decompiled output in ignored local folders. Commit only
  the original descriptions allowed by the [provenance rules](../re/PROVENANCE.md).

Unit IDs identify functions, states, native calls, replicated members and defaults. The
[inventory guide](../re/inventory/README.md) defines their syntax; the
[coverage tool guide](../tools/OpenScct.Coverage/README.md) explains how they count toward
documented, ported and verified coverage. None of these percentages measures C# line coverage.
