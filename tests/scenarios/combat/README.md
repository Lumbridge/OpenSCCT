# Combat scenarios

Two-player combat cases recorded in the original with the combat rig (`re/tools/difftest/combat.py`,
`docs/spikes/R4-combat.md`), and the single-player combat cases the movement recorder plays.

| Files | Format | Recorded by | Replayed by |
|---|---|---|---|
| `*.combat.json` | `svm.combat/1` (below; schema `svm.combat-1.schema.json`) | `combat.py record` | not yet: needs a two-player sim runner |
| `merc-fall-ledge.scenario.json` | `svm.scenario/1` (`../README.md`) | `record.py record` | the sim runner (`tests/OpenScct.DiffTest`), like the movement set |

The files are generated: edit `re/tools/difftest/combat_suite.py` and run it (`--check` fails when a file differs).
Traces stay local: `local/difftest/golden-combat/<id>/run<N>.jsonl.gz`, with `measure.json` per scenario and
`results.json` / `RESULTS.md` for the whole set.

## svm.combat/1

| Field | Meaning |
|---|---|
| `format`, `id`, `title` | `svm.combat/1`; `combat.<name>`, the file name; one line |
| `map` | the stock map; the rig map `<map>Combat` is built from it (a sandbox copy with the rig compiled in) |
| `mode` | `playlevel` |
| `local` | the local player: `team` (`spy` / `merc`), `floor` `[x, y, floorZ]`, `yaw`, `crouched` |
| `target` | the rig's target player (a second controller and pawn the rig makes as the game makes a player), the same fields plus `atStart` (left where RestartPlayer put it); `null` for none |
| `setup` | extra steps before the settle: `stance {crouched}` (local), `draw` / `holster` (the spy's gun), `refill {who}`, `rof {mode}`, `heal {who, hp}`, `clearHistory`, `targetRole {role}`, `wait {frames}`, `waitStill {maxFrames}` |
| `settleFrames`, `durationFrames`, `repeats` | as in svm.scenario/1 |
| `events` | frame-indexed events (below), in frame order |
| `measure` | what to measure (`hits`, `shots`, `reload`, `shock {victim: L or T}`, `energy`, `blast`, `states`; `re/tools/difftest/combat_measure.py`) |
| `covers` | coverage unit ids |
| `spot`, `notes` | free text |

Before every scenario the recorder removes the last target, puts the local player on its spot with full health,
a full rifle and full energy, spawns the target (team, spot, stance), heals it and clears its damage history.

Events (`frame`, `do`, arguments). Rig commands run in the rig's Tick of their frame; one per frame:

| `do` | Arguments | Effect |
|---|---|---|
| `aim` | `bone`, optional `to` (the middle of two bones), `offset` | the local view (MyDesiredRotation, its local copy and Rotation) at a target bone from the local eye |
| `aimPoint` | `point` | the local view at a world point |
| `fire` / `fireUp`, `altFire` / `altFireUp` | | what the Fire (AltFire) key does: the held button byte and the exec function once; up clears the byte |
| `rof` | `mode` (`single`, `burst`, `auto`) | the local rifle's rate of fire (single is otherwise only set by the scope) |
| `heal` / `healLocal` | `hp` | target / local Health |
| `place` / `teleport` | `floor`, `yaw` | target / local pawn on a spot (1 uu above floor plus collision height) |
| `crouchTarget` | `crouched` | the target's stance change (the crouch actions and transition state) |
| `grenade` | `kind` (`frag`, `smoke`), one of `at` / `fromTarget` / `fromLocal` (offset from that pawn's feet), `by` (`local`, `target`), optional `fuse` | spawns the grenade with that pawn as owner and instigator; its own fuse runs, the rig ends it a frame later if it has not |
| `targetFire` | `bone`, optional `offset` | the target's weapon fires at a local bone as its client would ask the server (spy: ServerFireWeaponWithLocation from its eye; merc: ServerSetAccurateRotation and ServerFireWeapon) |
| `spawnTarget` | as `target` | a new target (the old one removed) |
| `removeTarget`, `clearHistory`, `refill {who}`, `targetRole {role}` | | |
| `buttons` | `{name: value}` | local input bytes (`BT_Up`... `bCrouch`, `bUsing`, `bFireMode`, `bReload`, `bROF`...), held until changed |

## svm.combat-trace/1

Gzip JSON lines like svm.trace/1: a header (scenario and its doc, run, team, map and rig map hashes, the rig build,
binaries, field offsets per class, setup results), one line per frame, a footer (torn, gaps, late, unstable, fps).

| Key | What |
|---|---|
| `f`, `t`, `dt`, `wall`, `late`, `gap`, `tries`, `unstable` | as svm.trace/1 |
| `ack`, `st`, `sent` | the rig's last acknowledged command and its status; the commands written before this frame `[seq, do]` |
| `in` | local input bytes held this frame |
| `L`, `T` | the local player and the target (null when there is none): `loc`, `vel`, `acc`, `rot`, `phys`, `hp`, `crouch`, `ch`, `eye`, `bob`, `ps` / `cs` / `ws` (pawn, controller, weapon state), `wl`, `sticky`, `head`, `act` (sAction), `ik`, grab and KO flags, `crot`, `mdr` (MyDesiredRotation), `pad` (iDontUpdatePadInput), `stop`, `elec`, `tshock`, `tgrab`, `inv`, `prot` (fSpawnProtectionEnd), `taz`, `smoke`, `cough`, `fall`, timers, `btn`, `hist` (LastDamages: type, damage, pill, time), `anim` (channels: index, sequence, frame, alpha), `energy` (spy), `w` (weapon: class, clip, ammo, rof, bir, btf, disp, acc, tar, last, loc, rot) |
| `g`, `gs` | the rig's grenade (location, velocity) and its life (spawned, gone, rig fuse, last location) |
| `imp` | bullet impacts seen this frame (SFireTrace ImpactLoc, time, tracer start) |
