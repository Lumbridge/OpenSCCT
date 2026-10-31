# Pawn replication parity audit

Modern synchronization is an OpenSCCT design, specified in `spec/net/modern-synchronization.md`.
Original behavior remains documented in `spec/net/` and `spec/native/net-*.md`.

The shared presentation path captures every pawn channel, including plays and clears made outside
`ExecuteAction`. It transmits clip descriptors and phase, rather than bones. Proxies apply that state without
executing the action dispatcher, physics, gameplay notifies or damage. This is the replication coverage mechanism;
it does not by itself prove every original content sequence correct.

| Family | Authored state and authoritative outcome | Replication and recovery | Evidence / remaining validation |
|---|---|---|---|
| Walk, jog, strafe, idle, stance, turn, aim | `PawnMovementAnimation`, `ActionAnimation`, `PawnAnimation.Aim` | All channels, phase, alpha, cylinder, PrePivot, stance and aim on the same timeline | Four-viewer history comparison at 100/150/200 ms in both presets; rendered bot smokes at 100/200 ms |
| Jump, fall, landing, roll | `PawnActions`, controller movement states | Sequenced commands, action ordering, prediction history and replay; remote pose snapshots | `RubberBandTraversalTests`, `MoveIntegrationTests`, `PlayStepCorrectionTests`; root cursor handoff regression |
| Ledge catch, hang, shimmy, pull-up, drop | Ledge states and root motion | Physics/zone plus all authored channels and root locks | Recorded map traversals over loopback, C, 100/150/200 ms; late baseline and reset tests |
| Pipe, ladder, fence, hand-over-hand, zip, vault, wall jump | Traversal states and GE context | Ordered movement/control stream; complete pose baseline when first seen in traversal | 66 traversal cases; 12 first-seen physics modes; root-motion entry and wall-jump regressions |
| Moving bases and pawn collisions | Shared collision driver, base reference and base pose | Bounded historical pawn cylinders and mover poses; base-relative authoritative corrections; owner carry through base translation and rotation | `PredictionCollisionTests` checks recorded collisions, moving-base carry, live-world restoration and suppressed contact effects; dedicated moving-base latency recording remains required |
| Cooperative boosts and catches | `SPlayerController.Movement.Coop` and paired states | Authored channels/root locks plus base, interaction, readiness and paired pawn references; reliable partner-owner actions | Two independent owners boost and hoist on SteeE at 100/200 ms in both profiles, including immediate handoff; rendered bone-motion checks at both RTTs; sampled recording frames reviewed |
| Draw, holster, fire, reload, gadgets | Weapon/gadget actions and authoritative combat calls | Channel descriptors/phases, `WeaponLoaded`, replicated weapon actors and existing combat bridge | Local action/weapon tests and network combat/equipment checks; attachment-by-attachment historical phase comparison remains required |
| Hacking, bomb, disk and use menus | Objective interactions; server decides progress and outcome | Pose baseline, used object and interaction references; owner heartbeat/correction while stationary | Terminal/device network tests and renderer-logic interaction checks; exhaustive action matrix at each RTT remains required |
| Grab, choke, melee and release pairs | Combat grab/terminate states, server validation | Attack/attacker/interaction/release/termination references plus channel and root state; reliable victim-owner actions | Four-client two-owner grab/KO/recovery at 100/200 ms in both profiles, with historical clip/phase checks; rendered catch/KO/wake at 100/200 ms; other releases remain required |
| Damage, knockdown, knockout, death, wake-up | Damage pipeline, action animation, authoritative health | Health, sleeping/lying/hidden state, physics and complete active channels; native action flags apply to received batches too | Gameplay damage tests, first-seen KO weapon attachment and two-owner KO recovery; both roles' death poses, corpse blinking, attachments and respawn compared at 100/200 ms in both profiles; revival and rendered death comparison remain required |
| Sounds and effects | `SimEvents`, `NetHearSound`, `PawnSoundReplication`, `NetDeviceEvents`, combat calls | Reliable pawn play/stop events share presentation time; late event relay follows input callbacks; replay suppresses duplicates | 100/200/300 ms sound order, stop, missing-reference and allocation checks; existing device/shot/sound tests; exhaustive original sound/effect inventory remains required |

## Automated checks and recordings

`PawnSynchronizationTests` compares source history with the displayed server time on four independent clients.
It covers both rate presets at 100/150/200 ms, a 300 ms stress link, jitter, loss, duplication, reordering and a
250 ms directed outage. Separate baselines check every channel, traversal physics, late actor references and resets.
`ModernSynchronizationTests` covers stale snapshots, held velocity, monotonic adaptive playback and asymmetric links.
Same-tick play/clear batches are checked through the reliable receiver under loss, reordering and duplication.
Snapshots recover four preceding animation phase ticks, encoding deviations from linear playback; this preserves
the host-tick phase when arriving commands make the authority advance zero or several inputs in one host tick.
`IsPoseBuffered` distinguishes resolved tick history from estimated playback during a descriptor or sample underflow.
Reliable body-state keys retain transitions between movement snapshots. Historical stance, physics, readiness,
health, collision shape, attachment and paired references are compared at displayed server time in both presets.
A late paired actor resolves from the retained reference before its spawn acknowledgement returns to the server.
Owner latency cases include routine state acknowledgement refreshes and changing directed link delays.
`BotMatchTests.FourDedicatedClientsFinishAtHighPingWithChangingDirectedLinks` adds four independently driven
owners in both profiles at 100/150/200 ms, asymmetric links, a directed outage, stalled frames, no unexpected
prediction resynchronizations, resting placement and per-client bandwidth checks. The fence-root handoff regression
checks that post-animation actions belong to the completed movement command, including receipt recovery.
`ServerAllocationTests` retains the existing zero-allocation server gate; the pawn synchronization suite adds
capture/transition allocation and datagram-size assertions.
`StuckTouchTests` verifies replay cannot repeat trigger effects or remove current interaction contacts.
`PredictionCollisionTests` verifies moving-base translation/rotation and historical pawn cylinder collisions;
temporary collision staging restores the live actors before current contacts are reconciled.
`PawnSoundReplicationTests` exercises ordered loop starts/stops under loss, duplication and reordering, suppresses
replay emissions, expires missing actor references without stalling another pawn, and measures zero steady
allocations across both server and client while repeatedly transmitting sounds.
`PairedActionLatencyTests` starts a grab from its owner, completes a knockout release and wakes the victim. It checks
both owners, the two other viewers' paired references, exactly one health reduction, placement after recovery,
no unexpected resets, and buffered catch/release/wake clips against authoritative history within one simulation tick.
The victim owner's clips must include the knockout and wake; merely rendering those clips on observers is insufficient.
`CooperativeLatencyTests` completes the real SteeE boost and hoist with two owners in both presets at 100/200 ms,
including immediate boost-to-hoist handoffs. Both owners must settle within 2 uu and all four clients must receive
the paired clips without prediction resets. Cooperative readiness and grab/termination eligibility are explicit
snapshot state rather than inferred by executing gameplay on a proxy.
The lethal-damage cases compare real spy and merc death clips and attachment state against displayed server
history, including the corpse's blink timeline. They require one kill/death, a live respawn with placement within
2 uu, and no prediction resynchronization request. Dead pawns use the authoritative world clock and do not record
untransmitted owner heartbeats during the death sequence.

Rendered tests: `play-net100` and `play-net200`, three server bots and a local player through the actual play scene.
They measure the rendered skeleton over moving windows and optionally save a 10 fps image sequence. Their camera
follows a remote spy with collision checks, chooses an unobstructed angle and enables night vision for inspection.
Straight constant-speed segments also compare the actual rendered model origin with unquantized authoritative
history at the reported displayed server time. At least 100 rendered samples must stay within 2 uu, with no backward
movement. Sampling continues for a bounded interval if a slow renderer needs more frames; the error limit is unchanged.
Bone excursion is measured throughout each window, so an exact complete
walk cycle cannot be mistaken for a frozen skeleton. `play-net-dedicated100` and `play-net-dedicated200` use the
actual dedicated host with four network clients: a rendered local player and three independently predicted bot
clients. They also require local movement input to reach its pawn and no unexpected bot prediction resets.
`play-net-grab100` and `play-net-grab200` place two independent owners at SteeE's spy spawn, perform a grab and KO,
and wait for authoritative recovery while the fourth client renders them. They require catch/KO/wake clips,
stationary rendered bone motion during both KO and wake, a single health reduction and no prediction resyncs.
The test waits for the simulated recovery rather than assuming a wall-clock capture advances enough game ticks.

Run the smoke script with `-Tests @('play-net100','play-net200') -ExpectedFailures @() -Windowed -ShotsDir <folder>`.
Each multiplayer smoke also has a `-stock` variant that explicitly selects Stock on its host and clients.
Test and render logs for this implementation are retained under `local/test-results/netcode-*`.

## Validation record, 2026-10-31

Both solutions build without warnings or errors. These results distinguish the full-suite checkpoint from the
focused checks repeated after the final input-playout and encoding changes.

| Gate | Result | Evidence |
|---|---|---|
| Network library | 182 passed, no skips | `netcode-motion-interval-network.log` |
| Final movement, traversal, pose, collision, paired actions, sounds, rewind and allocation regressions | 144 passed, no skips | `netcode-final-current-regressions.log` |
| Owner stress and hub bandwidth | 12 owner cases plus the 50-visitor hub passed, no skips; includes 300 ms, 5% loss and directed outages | `netcode-final-current-owner-stress.log` |
| Final SteeE spy wins, A/B/C | 3 passed, no skips; busiest client 12,147 B/s, below 12,288 B/s | `netcode-final-current-budget.log` |
| Final four-client real UDP match | Passed, no skips | `netcode-final-current-udp.log` |
| Rendered multiplayer | All 16 gameplay scenarios passed, both presets at 100/200 ms; listen/dedicated movement, grab/KO/wake, boost/hoist | `netcode-render-final-current.xml` |
| Actual rendered constant-speed placement | At least 100 samples per movement scenario; worst error 0.863 uu, no backward jumps | `netcode-render-final-current-run.log` |
| Core / physics | 96 / 184 passed, no skips | `netcode-state-history-core.log`, `netcode-state-history-physics.log` |
| Full gameplay / renderer logic | 1,332 / 203 passed; two / one known baseline content failures, no skips | `netcode-state-history-game.log`, `netcode-state-history-logic.log` |
| Full integration checkpoint before final playout/encoding changes | 247 passed, one known baseline lobby failure, no skips; affected paths rechecked above | `netcode-state-history-integration.log` |

The 16 recordings are under `local/netcode-render-final-current/<scenario>/recording.mp4`. Their durations follow
the captured timestamps, preserving capture stalls rather than assuming every screenshot is equally spaced.
Sampled 100/200 ms frames were reviewed alongside the automatic rendered skeleton and placement checks.
The acceptance boundary below still defines the uncompleted work; these passing checks do not certify every original
content sequence or a complete authoritative controller-frame rollback.

## Acceptance boundary

The synchronization core and the coverage above are implemented. Full original-content parity is not certified by
synthetic channel tests or skipped content. The remaining action-specific rendered comparisons in the table
and content-specific moving-base latency scenarios must
pass before declaring the complete plan accepted. Reconciliation retains recorded transition context and temporarily
stages recorded pawn cylinders and mover transforms; it does not execute another actor's gameplay during replay.
Collision history is bounded at 128 pawns/movers per input frame; exhaustion requests a clean resynchronization.
The correction wire carries authoritative movement state. Controller, traversal and root/channel reconstruction
uses the corresponding recorded prediction context and newer reliable authoritative state entries, rather than a
complete serialized authoritative controller frame. Full authoritative controller rollback and paired gameplay
therefore remain an implementation and acceptance boundary, in addition to the rendered comparisons above.

At baseline commit `44c193f`, the current content already fails two gadget-menu English-text assertions, the Office
hack with a +30 uu use offset, and lobby revenge map selection (invalid game-mode data in `sim/gameplay.json`).
Each was reproduced in an isolated baseline checkout using the same content. They must be distinguished from new
netcode failures when assessing the full gameplay, renderer-logic and integration gates.
