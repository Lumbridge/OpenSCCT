# Networking improvement plan

Prepared 1 November 2026 for the reported PC/laptop LAN failures. Overnight execution was authorized and an hourly continuation heartbeat was created around 01:43 Europe/London, with an 08:00 work cutoff and a report on the first subsequent wake. Current status and evidence: [overnight report](NETWORK-OVERNIGHT-REPORT-2026-11-01.md). Planned items below are not claims of completed work.

## Outcome

A regular match starts only after its participating clients can play. A joining Hub player receives a coherent world before gaining control. Spies land correctly, both owners complete grabs, and observers see the corresponding animation and stance. Crouching must keep the spy correctly positioned above the supporting floor on every viewing client, including throughout stance transitions. Stance and rendered ground alignment are blocking correctness requirements in this pass. Each result must be reproducible through real game processes and accompanied by evidence.

Keep the current authoritative server, fixed simulation ticks, local movement prediction/replay, and buffered remote snapshots. Strengthen the boundaries between these systems. The most urgent architectural work is coherent state ordering and recovery across controller state, movement, collision shape, animation, and paired actions.

The code already has sequenced inputs, pawn epochs, replay histories, per-client budgets, lag compensation, and snapshot animation. Extend these mechanisms after checking their contracts; avoid implementing a second competing version. A transport replacement, higher tick rate, or additional smoothing is not a demonstrated remedy for the observed failures.

This is an assessment of this repository and its reproduced failures, not a claim of a universally best architecture. Godot's own guidance supports server authority with validated input and permits a custom low-level backend when that control is needed ([official networking documentation](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html#secure-multiplayer-design)). Valve's published implementation also explicitly accounts for interpolation when choosing rewind time and stops history traversal across death or large discontinuities ([Source SDK lag compensation](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/server/player_lagcompensation.cpp)). These are useful contract checks; they do not certify OpenSCCT's implementation. The local regressions and recorded games determine acceptance here.

## Evidence and current checkpoint

The current working tree includes unrelated user work. Preserve it. The checkout was based on `a12d374bef8ed2ec37c869391dd06c30f3466c88` during the investigation; confirm HEAD and the working tree when resuming. New branches must use `fix/`, `feature/`, or `enhancement/`.

| Finding | Evidence | Status |
|---|---|---|
| A client reports ready before its rendered play scene is loaded | `ClientSession.ReportWaiting` previously depended on controller availability; the scene load completes later | Implemented; delayed-load server traces pass in regular sessions and Hub. Expanded dedicated matrix in progress |
| A new connection can invalidate readiness during the start delay | `DedicatedStartGate` did not recheck during its delayed start | Implemented; recheck participants/readiness, exclude spectators, remove timed-out unready players. Focused regressions pass |
| A load stall can leave presentation seconds behind the server | A clock regression reproduced a ten-second interruption recovering only through slow slew | Implemented forward-discontinuity recovery with monotonic time; clock regressions and live stall checks pass |
| A valid grab can start on the server and fail to complete on its owner | Authority used world time for approach and older input time for catch, causing owner state rejection | Fixed monotonic world-clock state ordering; real grabs/KO/recovery pass in both role arrangements with an observer |
| Player stuck in falling pose after leaving a ledge | Grounded correction could retain the falling controller state | Implemented normal landing completion after replay; host and remote Aquarius Seaside drop checks pass. Physical laptop confirmation remains |
| Bent/sliding remote spy in regular sessions and Hub | Loading clocks, state ordering and geometry interpolation crossed incoherent boundaries | Several causes fixed; rendered movement/paired-action checks pass, but complete pose/traversal matrix remains open |
| Crouching spy visibly sinks below the ground | Snapshot interpolation blended collision centres while applying a discrete PrePivot; transient foot depth also exists in original clips | Mesh-anchor interpolation fixed. Strict rendered floor checks still fail around 12–14 uu; original bone captures reproduce this toe depth. Keep this boundary explicit |
| Dead owner remains floating or misplaced | Autonomous physics stopped with input prediction at death | Fixed; four grounded/airborne role regressions and real death pose checks pass |
| Death/spectating HUD omitted outcomes | Killed/visitor states lacked the metadata super link the HUD assumed | Fixed shared spectator query; live watched-player/countdown/no-respawn screens verified |
| Slow rendered loads disconnect before the session's load deadline | Normal 15-second idle timeout expires before the 60-second scene-loading allowance | Fixed bounded per-login grace, cleared on readiness; silent/connected timeout regressions and dedicated 20-second load pass |
| Active input recovery falls behind at the normal client bandwidth floor | Real Aquarius trace and both role regressions at 5 KB/s reproduce >1-second pending input | Compact independent input batches implemented; focused regressions pass, extended real-process acceptance in progress |
| Packet reordering discards unique reliable controls and sequenced input | Aquarius traces distinguish reliable-fence stalls from missing input gaps | Bounded late delivery through reliable channels and an explicit reorder-safe movement extension; 202 network and 76 selected integration tests pass, limited rendered three-minute test passes, hour soaks in progress |
| Dead player's camera sticks after watched teammate dies | Four rendered merc clients retain no target despite a living teammate | Missing friend/visitor RPC forwarding fixed; both role four-client progression and visitor regression pass |
| Departed teammate remains in spectator life total or as a destroyed camera target | Rendered merc disconnect shows stale total; two-world tests retain destroyed target | Deleted-target checks and authoritative post-removal aggregate refresh fixed; seven spectator integration tests and rendered merc disconnect pass |
| Corrected landing charges the same input tick twice | New role regressions and three full-suite infirmary failures report excessive movement time | Landing completion now preserves the remaining delta correctly; regressions and a subsequent full suite pass |
| Normal primary weapon spends ammo only on its owner | Real rifle owner reports 24/124 while server remains 25/125 | Shared authoritative accounting/reload and owner reconciliation added; real standing/crouched/automatic-fire cases pass |
| Jitter rejects valid automatic shots arriving together | Six owner shots become five accepted rounds in a rough-link run | Owner simulation shot clock with bounded lead and cumulative weapon interval validation; regression and real repeat pass |
| Different character animations/meshes can join the same game | Connected-world mismatch regression joins before the fix | Gameplay handshake checks both roles' animation JSON and skeletal GLBs; regular rematch and Hub rejoin pass with matching content |
| Delayed damage from an old pawn kills its replacement | Both roles consume a second life after an eleven-second directed delay | Combat requests carry original possession identity; both role regressions and rendered spy/merc repeats pass |

The screenshot's 37.5 uu correction matches the spy's standing/crouching height difference. This is a useful hypothesis for collision-shape or landing-state disagreement, not a confirmed diagnosis.

Work already added:

- `tools/network-lab.ps1` and `game/Scripts/Smoke/NetworkLabSmoke.cs`: two to four separate Godot processes, real UDP, isolated settings, key/mouse timelines, intentional stalls, screenshots, JSON state samples, JSONL traces, and owned-process cleanup. Match/Hub paths exist; a separate dedicated lab server is being validated.
- Host-thread inspection queues in `ListenHost` and `HubHost`, avoiding unsynchronized reads of the authoritative world.
- Explicit fixture placement and facing commands, distinct from gameplay input. A setup command is not evidence that ordinary player input works.
- Readiness and presentation-clock regression tests; independent server/client map-loader jump coverage.
- A support fix for valid float/ushort vertex colours in the TestBox render path; six format/stride regressions pass.

Validation recorded before this plan: the game solution built; the synchronization/clock test selection passed 17 tests; the lobby/pawn/paired-action/rendered-loader selection passed 48 tests. A headless two-process TestBox jump scenario passed. A rendered Aquarius session was driven and captured. These checks do not establish that the reported grab, ledge, and remote-animation defects are fixed.

Useful artifacts, relative to the repository:

- `local/network-lab/20261101-005102-502499dd`: passing two-process jump.
- `local/network-lab/20261101-005202-1abc05cb`: rendered Aquarius startup, movement and grab investigation; screenshots under `shots`.
- `local/network-lab/20261101-005637-035b0d86`: decisive TestBox grab trace. Around client ticks 8513–8527, the authority progresses from approach to catch while the owner remains in approach; around 8688–8695 it returns to walking and releases the pair.

The last controlled pair was stopped. Do not reuse its PIDs. Run manifests include process start times for ownership checks.

## Execution order

### 1. Make the network lab a trustworthy regression gate

Finish this first so every later change can be assessed against the same reproduction.

- Record build/binary, protocol, map, gameplay and character-animation content fingerprints, dirty source status, seed, role assignment and fault profile in each run. Commit ID alone cannot identify an uncommitted build.
- Use network GUID plus map/pawn lifetime for cross-process correlation. The existing `ActorId` values are local and must not be compared directly between worlds.
- Record commands, acknowledgements, state transitions, authoritative revisions, input sequence/ack, correction reason and magnitude, controller state, physics, collision shape, animation channels, root motion, paired references, and presentation age. Keep the host tick with each server sample.
- Compare a displayed remote pose with authoritative history at its displayed tick, not with the latest server position. Compare an owner correction at its acknowledged input tick.
- Make startup assertions observe the loading interval. Merely finding two pawns at the end does not prove that the host waited.
- Make jump/drop assertions require airborne samples and subsequent stable landing. A final walking state alone can pass when the input never worked.
- Add `grab`, `ledge`, `stance`, and `remote-pose` scenarios. Grabs must assert both owners and an observer, including the hold and release, rather than only seeing a request accepted. Stance scenarios must inspect rendered ground alignment during transitions as well as settled poses.
- Add bounded command deadlines, process-liveness checks while waiting for acknowledgements, error summaries, trace retention limits, and cleanup after every failure. Classify engine shutdown warnings separately from gameplay failures.
- Integrate existing `PlayInspector`/`InspectPose` checks and rendered captures. Ensure intentional test teleports are excluded from ordinary correction metrics, with a recorded settling boundary.
- Bind isolated Hub tests to loopback too. The regular-session harness already requests loopback-only hosting; the Hub path needs equivalent explicit isolation.

Deliverable: one command produces a pass/fail report, both logs, aligned traces, and rendered evidence when requested. A missing input, missing pawn, dead process, or unobserved action fails the scenario.

### 2. Complete startup, late join, and recovery

Owners: `ClientSession`, `OnlineClient`, `OnlineServer.TickLoading`, `DedicatedStartGate`, `PlayRoot`, Hub join/session paths, `PresentationClock` and `NetTimeline`.

- Separate connection, simulation content loaded, render scene ready, baseline applied, and playable states. Carry readiness in the current map/session epoch so travel cannot reuse an old ready flag.
- For a regular match, hold gameplay and the match clock until the expected active roster is ready. Agree on a server start tick after readiness; record client observation and input enablement. Test a joining/disconnecting participant during the start delay.
- Complete timeout handling: the current `TickLoading` only treats a missing `Slot` as missing. A connected client whose renderer never becomes ready must be handled explicitly. Remove failed participants, recalculate the active-player threshold excluding spectators, and return to the lobby if nobody can play.
- For the Hub, keep the existing world running while gating the joining player's control and visibility until its baseline and scene are ready. Simultaneous world startup and joining an established Hub are separate scenarios.
- Reset presentation/correction history deliberately on travel, possession, respawn and large timeline discontinuities. Obtain a fresh coherent baseline when retained history is insufficient. Avoid blending across a teleport, respawn or stale animation lifetime.

Acceptance: 0/5/15-second client loads and a timeout case; host and guest swapped; regular match, Hub initial join, Hub late join, disconnect during loading, and second-round travel. A five-second resumed client stall should return to a valid buffered timeline within one second after fresh packets resume; this is an initial lab target to verify, not a published performance claim.

### 3. Correct movement and state ordering together

Owners: `SequencedMoveTransport`, `ClientMoves`, `ServerMoves`, `NetMovement.Prediction`, `SPlayerController.StateSync`, movement/landing handlers and `Level.CommandTime`.

- Reproduce the Seaside ledge failure with crouched spawn, stand/jump/drop transitions, and different client render rates. Include walking off a ledge without Jump, low ceilings, stairs, slopes and moving bases.
- Audit which clock owns each event. Movement replay can use input time; authoritative state ordering must remain monotonic across input-time and world-time callbacks. Inspect mirror-state timestamps and acknowledgement barriers before changing them.
- Introduce or extend an authoritative state revision scoped to the pawn/map lifetime if the reproduction proves current timestamps insufficient. Revisions and input acknowledgements must have explicit, distinct purposes.
- Reconcile location, velocity, grounded/base state, collision height/zone, crouch flag, mesh offset (`PrePivot`), movement action, controller state and root-motion context coherently. Ensure replay cannot restore an older falling/crouching controller state after a newer authoritative transition. Audit the standing/crouching centre-height adjustment and visual offset together so a height change cannot be applied twice or applied to only one side of the transition.
- Retain responsive local input and bounded prediction history. Missing or expired history should trigger a deliberate recovery path with a reason, not repeated corrections forever.
- Add transition invariants and diagnostics: a landed pawn must not remain in a falling animation/controller state; crouch geometry must agree on both sides after settling; repeated identical height corrections must be visible as a failure.

Acceptance: repeat each reported movement transition under LAN and impaired links; no permanent falling state, repeated 37.5 uu oscillation, unexpected history reset, lost landing transition, or crouch-induced sinking. Check collision and rendered alignment together using the stance criteria in step 5 before declaring this step complete. Initial controlled-fixture target: settle within 2 uu of authority after the correction window, measured on corresponding ticks and excluding explicit fixture placement.

### 4. Make paired actions authoritative and ordered

Owners: combat grab/release states, `NetCombatBridge`, state sync, `NetMovement.OwnerActions`, pawn reference replication and command simulation boundaries.

- First preserve the existing TestBox failure as a failing test driven by Use. Place the spy 85 uu behind a standing merc, face both the same way, then use the ordinary input path. The trace must distinguish geometry rejection, server rejection, state receipt, state application, replay rollback and approach timeout.
- Trace the server's catch transition end to end: send, ordering, receive, application, and any subsequent prediction restore. The current evidence does not yet distinguish timestamp rejection from rollback or another delivery defect.
- Give a paired action a consistent authoritative identity/lifetime and phase shared by both pawns. Reuse existing epochs/revisions where suitable. Apply partner references before a state requires them, and reject stale releases or actions from a previous pawn lifetime.
- Keep server validation for distance, facing, obstruction, eligibility and action timing. Add explicit debug rejection reasons. Do not widen reach or bypass collision merely to make tests pass.
- Cover catch, hold, carry, KO, neck break, cancellation, victim crouch, death/disconnect during the action, and respawn. Extend the same ordering contract to cooperative boost/hoist where tests expose a shared fault.
- Audit the existing lag-compensation path before extending it to melee. Any rewind must be bounded and validated against server history; paired animation itself follows the accepted authoritative action.

Acceptance: both owners and an observer agree on the pair and phase, a hold actually occurs, release resolves once, health changes once, both pawns recover, and replay produces no duplicate effects. Hub attacks remain disabled by Hub rules.

### 5. Validate and repair rendered remote animation

Owners: `PawnStateReplication`, animation descriptors/state histories, `PresentationClock`, pawn visual interpolation, root motion and aim/IK application.

- Sample transform, stance, physics, animation channels, root-motion phase, weapon/attachment state and aim from compatible authoritative history. Do not combine an old animation pose with a new collision shape or attachment lifetime.
- Treat stance changes as a coherent transition: pawn centre, collision height, zone, crouch flag, `PrePivot`, animation/blend phase and rendered root placement must describe the same transition and displayed time. Check for discrete shape changes combined with an interpolated centre or stale pose. The supporting foot position should remain consistent with the authored movement and supporting surface.
- Verify baseline completeness on first spawn and late join, including asset/clip mappings. A content mismatch needs an actionable diagnostic rather than a silently malformed pose.
- Inspect presentation discontinuities after loading and packet recovery. Clear obsolete blend/correction history where required while retaining ordinary jitter smoothing.
- Measure actual rendered bones and feet while the remote spy walks, runs, crouches, jumps, lands, aims and performs a paired action. Capture short frame sequences from the other player's camera, and inspect them.
- Add a dedicated stance matrix: crouched first spawn; stationary crouch/stand; moving crouch/stand; repeated toggles; blocked standing under a low ceiling; crouched ledge drop and landing; stairs, slopes and moving platforms; joining while another spy is crouched or changing stance; packet recovery during a transition. Run in regular sessions and Hub with host/guest roles reversed. Include merc stance coverage for shared code.
- For each stance sample, record authoritative history and the owner/observer presentation: collision bottom, support surface/base, actor and rendered root positions, `PrePivot`, foot/toe world positions, active clips/blends and displayed tick. Capture before/during/after images so a correct final frame cannot hide transient sinking or a malformed blend.
- Close the inspector's false-pass gap: `InspectPose.Feet` currently skips samples when the collision bottom is too far from its floor probe. Add a separate grounded collision/support check and count missing probes/feet and skipped samples. An entirely sunken pawn or an unmeasured transition must not pass because the foot check skipped it. Use the displayed support transform for moving platforms and distinguish legitimate airborne/action poses explicitly.

Acceptance: motion does not leave a frozen locomotion skeleton, no invalid bone transforms, standing/crouching feet align with the collision floor within the existing inspector limits, and buffered clip phases agree with source history. Crouch/stand transitions must not sink or float because centre height, collision shape, mesh offset and animation disagree. Check every rendered transition frame against declared geometry/pose tolerances; retain the worst frame and the corresponding state, and report coverage counts. Do not loosen tolerances to accept the reported defect. Rendered evidence is mandatory for both the bent/sliding and crouch-under-ground reports.

### 6. Exercise real adverse links and leave a repeatable gate

- Add a seeded, direction-specific UDP impairment wrapper or proxy to the real-process lab: delay, jitter, loss, duplication, reordering, bandwidth limits and short outages. Keep fault injection opt-in and scoped to lab traffic; no machine-wide network changes.
- Support a separate dedicated server and at least three clients so two interacting owners and an independent observer can be checked. Exercise both the host-as-spy and host-as-merc arrangements.
- Run a small required matrix, then a longer soak after correctness fixes stabilize. Verify actual injected profiles and seeds in the report.

| Profile | Initial lab settings | Purpose |
|---|---|---|
| LAN | 0–24 ms RTT, no synthetic loss | Reproduce the user's failures without masking them with latency |
| Normal remote | 100 ms RTT, 10 ms jitter, 1% loss | Input recovery and ordinary presentation buffering |
| Difficult remote | 200 ms RTT, 30 ms jitter, 3% loss; reorder/duplicate cases | Ordering, paired actions and bounded recovery |
| Asymmetric | Different uplink/downlink delays and bandwidth caps | Separate input timing from presentation timing |
| Interruptions | 250 ms/1 s packet outage; 5 s client main-thread stall | Recovery, baseline refresh, no stuck action |

Start with TestBox, then Aquarius and an existing traversal map. Cover 30/60/144 FPS where the renderer supports it; include a high-FPS smoke because the report was captured above 300 FPS. Fixed simulation ticks must not depend on render FPS. Exercise both rate presets where shared synchronization code changes.

Acceptance: required scenarios pass repeatedly with recorded seeds. No unexpected disconnect, unbounded queue/history growth, duplicate damage, stuck paired state, permanent falling or persistent correction loop. Measure bandwidth, tick time, correction frequency and magnitude, replay depth, presentation age and missing-pose counts. Set performance budgets from the measured baseline and existing allocation/datagram tests; do not invent a favorable threshold after seeing a regression.

## Additional release checks

### Death, surviving teammates, and spectating (added by Ryan, 02:31)

Treat this as a required acceptance area for both spies and mercenaries, with original-game rules as the reference. Establish the outcome separately for remaining personal lives/respawn, two or more living teammates, exactly one living teammate, and no living teammates. Do not infer a team wipe from a temporarily empty or stale client roster. Include simultaneous final deaths, a teammate disconnecting while another dies, respawn delays and exhausted lives, and the final round result.

- Trace the original death/KO distinction, death state and animation, lives accounting, respawn eligibility, observer selection, and match-end handlers/data before changing outcomes. State which outcomes are verified from the original and which remain unresolved.
- Check authoritative decisions and the victim, teammate, opponent and independent spectator views. Damage and lives must resolve once; a dead or spectating player must not remain able to move, shoot, grab, interact with objectives, or receive stale owner commands after respawn.
- Verify entry into spectating, choosing and switching valid living teammates, the last teammate dying, no valid targets, and returning from spectating on respawn or the next round. The UI must clearly explain waiting for respawn, remaining lives, spectating and the round outcome.
- Inspect actual rendered death/KO/respawn transitions for both roles: standing, crouched, airborne, stairs/slopes, paired grabs and traversal. Require clean supported final poses, no floating bodies, invalid skeletons, lingering attachments, camera jumps into geometry or stale death overlays. Keep before/during/after evidence from multiple viewers.
- Run LAN and impaired-link cases, including death during a packet outage, delayed death replication, spectator join, and respawn with old movement/state packets in flight. Use pawn lifetimes to reject stale data, and test second-round travel.

Acceptance: correct original-rule outcome for each survivor/lives case; exactly-once health/lives/round decisions; coherent spectator targets and UI; clean rendered death, recovery and respawn with no continuing dead-player input. Mark unrun combinations explicitly in the morning report.

Requested scope: make networking dependable across complete matches, including failures that a correct-looking host view can conceal. Run these after establishing the core reproductions, and include relevant cases immediately when changing a shared subsystem. Existing test files and historical passing results are starting points, not evidence that the current real-game cases pass.

| Area | Required scenarios and acceptance |
|---|---|
| Hit registration and stance | Shoot standing, crouching, transitioning, jumping and moving targets from both host and guest. Compare the shooter's recorded displayed pose with the historical hit geometry used by the authority. Verify body/head regions, actual health, hit feedback and kill attribution. Test cover and moving doors against an explicit rewind policy. A visually corrected crouch with an incorrect hit shape still fails. Rewind remains bounded and cannot cross a respawn/teleport lifetime. |
| Weapons and gadgets | Fire, reload, switch weapons, draw/holster, interrupt a reload with damage/death, and use representative projectile/deployed gadgets. Owner, authority and observers must agree on the weapon, attachment, ammo, cooldown and action result. Packet recovery must not create extra shots, ammo, projectiles, explosions or repeated damage. Distinguish predicted effects from authoritative acceptance/rejection. |
| Shared world and objectives | Doors, lifts/platforms, switches, lights, terminals and objective timers must converge on the same state. Test two players attempting the same interaction, a participant leaving mid-interaction, and joining or regaining relevancy after the object changed. Collision must agree with the visible door/platform, and an objective completes and awards its result once. |
| Pawn and session lifetimes | Exercise KO/wake/revive, death/respawn, spectating, team changes, round end/revenge, map travel and reconnect. Inject delayed movement, stance, damage and paired-action messages from the prior lifetime. They must not control or damage the replacement pawn. No ghost actors, duplicate spawns, stale attachments or inherited input. Host disappearance must produce the supported disconnect/recovery behavior; adding host migration is outside this pass. |
| Real client interruptions | Separate network loss from a rendering hitch, low FPS, minimized/alt-tabbed window, lost input focus and a host simulation stall. Keys held during focus loss must release correctly. Recovery must not generate a movement burst, fast-forward gameplay timers incorrectly, or leave a character attacking/walking forever. Test mismatched host/client frame rates and repeat using the physical PC/laptop build. |
| Slow-client isolation and load | One delayed/loading/bandwidth-limited peer must not starve healthy peers or overload reliable queues. Measure simulation tick duration independently of render FPS. Exercise supported match capacity and a representative busy Hub, late joins, and voice traffic with local synthetic audio where supported. Preserve gameplay delivery under cosmetic/audio traffic and apply bounded recovery/disconnection policies when queues cannot recover. |
| Sound, effects and information | Replay/retransmission must not duplicate footsteps, weapon sounds or impacts; looped effects must stop on action end, death, travel and disconnect. Test invisibility/vision/team/relevancy transitions against game rules, including stale remote meshes or attachments when a player disappears and reappears. Rendered evidence and event counts must agree. |
| Protocol and input robustness | Verify version/content mismatch rejection, including character clips and gameplay collision data; stale epochs; duplicate/reordered commands; invalid/non-finite coordinates; out-of-range action identifiers; excessive input rate; bounded packet sizes and histories. The authority must validate movement/action constraints and remain responsive to healthy clients. Keep malformed-traffic tests local and isolated in the lab. |

Existing useful entry points include `LagCompensationEpochTests`, `PredictionCollisionTests`, `MoverNetworkTests`, `PawnSoundReplicationTests`, `OnlineUdpTests`, lobby/bot tests, and the gameplay combat/equipment suites. `NETCODE-PARITY.md` explicitly records that complete authoritative controller-frame rollback is not certified; investigate that boundary directly rather than interpreting its historical test totals as comprehensive correctness.

For every scenario, assess the owner, the other player, an independent observer where applicable, and the authority. Reverse host/guest roles and run both listen and dedicated paths for changes shared by those paths. A correct server variable or a good host screenshot alone cannot satisfy a rendered or input-path assertion.

Add a bounded long-run test after the focused fixes: repeated stance changes, movement, combat, respawns, joins/leaves and round transitions with retained seeds and periodic state/memory/queue summaries. Reserve roughly one to two hours if the core fixes are stable; record the actual duration and load. Extend the soak on a later run if the engineering night is consumed by a correctness blocker. Capture the command sequence, initial fixture/build identity and directed delivery/fault timing for failing runs so they can be replayed or reduced to a shorter regression.

Completion means zero known blockers in the reported scenarios, repeatable passes for the declared supported matrix, and an explicit list of remaining unverified cases. A finite overnight run cannot certify perfection across every network, machine and content sequence. Report `PASS`, `FAIL`, `SKIPPED` and `NOT RUN` distinctly, including coverage counts; do not count a missing render capture or a skipped assertion as a pass.

## Overnight timeboxes and handoff

Use roughly one engineering night as an initial budget, with this order: lab assertions and failing reproductions; lifecycle/clock fixes; movement and stance/ground alignment; grab ordering and the remaining rendered-animation cases; impaired-link verification and soak. Stance checks from step 5 run alongside movement work, not only at the end. Investigations may consume the budget. Correctness blockers take priority over bandwidth tuning or adding matrix combinations.

After each meaningful fix:

1. Preserve the failing reproduction and its before/after trace.
2. Add a focused regression at the lowest useful layer and rerun the corresponding real-process scenario.
3. Check the diff, build the affected solution, and run the required related suites. Broaden checks when a shared protocol/state contract changes.
4. Record exact commands, run directories, outstanding failures and the next action in this document or a linked results note. Preserve unrelated working-tree edits.

If a protocol payload changes, update its compatibility/version contract and verify both peers reject mismatched builds clearly. Prepare a tested build and evidence for PC/laptop validation; local loopback and injected network tests do not prove behavior on the physical laptop/Wi-Fi link. Publishing or deployment is a separate task.

Morning handoff: fixes completed, unresolved defects with reproducible commands, changed architectural contracts, build identity, test/matrix results (including the additional release checks), rendered evidence, measured soak duration/load, and the short PC/laptop confirmation checklist. Report skipped, failed and unrun cases explicitly. Do not declare the networking reliable from unit tests alone.

## Starting commands and sources

Current lab commands (see the report for which combinations are actually validated):

```powershell
pwsh tools/network-lab.ps1 -Action Run -Map TestBox -Scenario jump
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario stall -Windowed
pwsh tools/network-lab.ps1 -Action Start -Map AquaE -Windowed -LoadDelayMs 5000
pwsh tools/network-lab.ps1 -Action Start -Hub -Windowed
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario ledge -ReverseRoles -Windowed -NetworkProfile rough
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario death -Lives 2 -SecondObserver -AirborneDeath -Windowed
pwsh tools/network-lab.ps1 -Action Run -Map AquaE -Scenario elimination -Lives 1 -Windowed
pwsh tools/network-lab.ps1 -Action Run -Dedicated -Map AquaE -Scenario startup -LoadDelayMs 15000 -Windowed
pwsh tools/network-lab.ps1 -Action Status -RunDirectory <printed-directory>
pwsh tools/network-lab.ps1 -Action Stop -RunDirectory <printed-directory>
```

The `startup` runner examines authoritative samples during the injected loading interval. The Hub selects its own world; the regular-session `-Map` argument does not change it. Fixtures are labeled separately from normal gameplay input. Dedicated mode shares the evidence format and uses its own loopback server process.

Repository references: [Netcode](NETCODE.md), [existing coverage and gaps](NETCODE-PARITY.md), [real input and rendered inspection](TESTING-PLAY.md), and [two-PC testing](NETWORK-TEST.md). Read code when documentation and current behavior differ.

External design background: Glenn Fiedler's [Snapshot Interpolation](https://www.gafferongames.com/post/snapshot_interpolation/) explains buffering authoritative snapshots to absorb jitter and the latency tradeoff. This supports retaining buffered remote presentation; it does not establish the cause of our bugs. [Valve's server lag-compensation implementation](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/server/player_lagcompensation.cpp) is a reference for bounded historical validation, not a prescription to adopt Source's exact gameplay tolerances.
