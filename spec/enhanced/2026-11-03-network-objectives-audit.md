# Enhanced 4.0 Beta 5: rounds, objectives, devices, networking and demos

Date: 2026-11-03. This is a read-only audit of the current rewrite, with a comparison of the locally supplied
Beta 3 and Beta 5 script packages. It does not claim runtime equivalence with Enhanced. No original-game code,
assets or decompiled bodies are included here. The package comparisons remain in ignored local storage.

## Evidence and boundary

- Beta 5 input: `C:\Users\ryans\Desktop\Enhanced SCCT Versus 4.0 Beta 5`.
- Beta 3 input: `C:\Users\ryans\Desktop\Enhanced SCCT Versus v4.0 Public Beta 3`.
- Decoded comparison evidence: `local/enhanced-b5-audit/uc-diffs/SBase/SGameInfo.uc.diff`,
  `SBase/SAlarm.uc.diff`, and `Engine/SPlayerStats.uc.diff`; complete extracted units are under the sibling
  `b3-uc` and `b5-uc` directories. These are local-only evidence, not files to commit.
- The demo-view comparison additionally uses the new view-update declarations in the local
  `SBase/SPlayerController.uc.diff`. The native implementation of that relay is outside this workstream.
- Current implementation inspected: game lifecycle, objective interactions, alarm states, kill statistics,
  online rejoin, replicated-function dispatch, voice routing, demo parser and playback.
- Validation ran only against the owned rewrite. No public servers, master services, original-game sessions,
  adversarial network traffic or exploit probes were used.
- Security observations below are **static review findings**. The controller RPC declarations, general
  dispatch and concrete handlers were traced, but malicious-client reachability was not demonstrated over
  the wire. Treat them as defensive validation work, not as a claimed exploit reproduction.

## Beta 5 changes worth adopting

| ID | Priority | Beta 5 behavior, decoded from the supplied package | Rewrite assessment |
|---|---|---|---|
| N01 | P1 | A network side with no surviving participants loses when its final player quit. The end check also records whether a non-spectator quitter belonged to each side. | **Missing, definite parity gap.** `src/OpenScct.Game/SBase/SGameInfo.EndGame.cs:43` excludes quitters before counting; line 98 then requires a remaining participant on the depleted side. The old behavior is explicitly asserted by `tests/OpenScct.Game.Tests/Game/EndOfRoundTests.cs:117` (`A_side_whose_players_all_quit_is_not_dead`). |
| N02 | P2 | A simulated alarm while listening still notifies the instigator, marks the device and activates the alarm, but does not add a real detection to the spy's alarm count. | **Missing, definite parity gap.** `src/OpenScct.Game/SBase/SAlarm.s_Listening.cs:42` calls the ordinary Trigger path, which calls `CountSpyAlarm` at line 32. `SAlarm.cs:330` increments `Spy_AlarmsTriggered`. A noise emitter can therefore pollute the real-detection statistic. |
| N03 | P2 | Enemy kills populate hip-fire, sniper, termination, mine and grenade causes for the merc and the spy victim. | **Mostly already supported independently.** `src/OpenScct.Game/SBase/SGameInfo.Feats.cs:38` performs this accounting and feeds achievements too. Do not add duplicate increments. See the remaining cases below. |
| N04 | P2 | Poison deaths are classified as mine deaths; sticky-blast deaths get the dedicated victim counter. | **Missing in the reviewed path.** `SGameInfo.Feats.cs:103` returns for a poisoned victim or other damage types after the synchronous explosion/termination branches. No writer for `Spy_DeathFromJumpingTasedMerc` was found in the current game code. Its generated field already exists. |
| N05 | P2 | Kill classification uses controller/team statistics even when the killer has no current pawn; the new firing/termination context fields identify the active cause. | **Partial architectural overlap, edge case to verify.** The rewrite has scoped `GadgetBlast.CurrentIsMine` and explicit termination/bullet tracking, which are preferable to a global scan for an exploding actor. However, `SGameInfo.Feats.cs:62` requires the killer's current pawn to be a merc before awarding grenade/mine/weapon counters. A posthumous grenade or mine kill can miss the subtype counters if possession is already gone. Runtime reproduction remains open. |
| N06 | P2 | Inventory initialization clears the statistics' weapon and gadget list before rebuilding the current kit. | **New statistics feature, not an inventory-spawning fix.** Current `SGameInfo.Spawn.cs:343` delegates inventory setup but has no corresponding stats list. If loadout statistics are added, clear them on spawn and also handle OpenSCCT's mid-round gear change (`SGameInfo.Gear.cs:32`). |
| N07 | P2 | `SPlayerStats` adds ammo-stock uses, spy-bullet targets, the weapon class and gadget-class list. | **Missing counters/metadata.** The current `USPlayerStats` schema lacks these additions; `SPlayerController.Devices.cs:48` refills ammo without an ammo-stock counter. Add their collection, match serialization and presentation together if desired; schema fields alone do not provide parity. |

N01 is the clearest immediate gameplay benefit from these three changed units. Reproduce it with a running
Story round, an untaken objective, one player per side, and a disconnect of the final spy or merc. Assert the
winning side, one result publication, and consistent server/client results. Cover spectator departure,
disconnection with a teammate still alive, simultaneous departures and Merc Deathmatch separately.

The existing online documentation says a round ends when the last person on one side leaves
(`docs/ONLINE.md`, “Rejoining a round”), while the old test and implementation explicitly preserve the round.
Adopting the Beta 5 end check resolves that contradiction. Reconcile it with OpenSCCT's rejoin policy: once the
round has ended, that departed player should not be offered a playable rejoin to the completed round.

For N02, compare an ordinary detection with `eventSimulateActivated` from the listening state. Both should
activate the same alarm and device marker, but only ordinary detection should increment the real-alarm stat.
Already-active and defender-present states need separate assertions; this Beta 5 edit changes the listening
state only. The old rule at `spec/devices/alarm.md:72` needs updating when the behavior is adopted.

For N03–N07, preserve the rewrite's existing independent accounting and fill only the missing cases. Beta 5's
new global exploding-actor search is not necessary when the rewrite already has a scoped damage source. An
explicit cause passed through lethal damage would also resolve posthumous kills without depending on the
killer still possessing a pawn. Verify poison death, sticky blast, posthumous explosive kills, ordinary weapon
kills, environmental kills and respawn/gear-change loadout replacement without double counting.

## Broader rewrite findings

### N08 — P1: objective RPCs need authoritative validation

`InteractionInit`, `Interaction` and `InteractionPost` are declared client-to-server controller functions in
`src/OpenScct.Game/Generated/SBase/SPlayerController.g.cs:3767`. `NetClasses.Create` retains the generated
layouts (`src/OpenScct.Server/NetClasses.cs:23`). `NetDriver.ReceivedFunction` checks ownership of the receiving
controller and the replication condition (`src/OpenScct.Net/NetDriver.cs:1397`), then dispatches the C# handler.
That is a useful ownership boundary, but it does not validate an interaction actor supplied as a parameter.

`src/OpenScct.Game/Objectives/InteractionMenuRules.cs:196` only rejects a missing/deleted object before invoking
its initialization. The subsequent two methods likewise invoke the supplied object. The normal switch
initialization at `src/OpenScct.Game/SBase/STriggerInteraction.cs:42` can fire its event without checking the
instigator's distance, team, alive state, selection, reuse delay or `bUsable`; those checks belong to the
normal menu-selection path and are not repeated here. No overriding server handler for these three functions
was found in the inspected server registration sites.

This is inherited script trust, not a newly found Beta 5 change. Add server-side authorization at the
interaction boundary, using the current authoritative pawn and target. Initialization should establish an
interaction session; continuation and cancellation should refer to that session. Allow legitimate movement
and latency tolerance without letting arbitrary target parameters bypass it. Regression coverage should
exercise valid nearby use, invalid state/team/target, stale continuation after respawn, reuse delays and
single-use switches. Network reachability remains unverified in this audit.

### N09 — P1: gameplay-object damage trusts client-supplied damage parameters

`DestroyGPO` is another generated client-to-server controller function
(`src/OpenScct.Game/Generated/SBase/SPlayerController.g.cs:3759`). Its implementation at
`src/OpenScct.Game/SBase/SPlayerController.Devices.cs:39` forwards the supplied object, damage amount and
damage type directly to `eventTakeDamage`, using the caller's pawn as instigator. The receiver ownership check
does not establish a valid merc charge or contact with that object. The inspected registration sites do not
replace this handler with a validating one.

Derive the damage/type from the server's active attack and validate target/contact/current possession before
applying it. The existing device test `TheMercChargeDamagesAGameplayObjectFromThePlayersPawn` covers a valid
direct call only. This is a static defensive finding, with malicious wire reachability unverified.

### N10 — P1: voice-target selection lacks the full binocular/scope gate on authority

`src/OpenScct.Game/SBase/SPlayerController.Voice.cs:187` checks the target's team and a 7,000-unit distance
when accepting a voice target. It does not check binocular/scope state, the current view trace, line of sight
or continued validity. The honest client performs its trace in `BinocularVoiceTrace` at line 254 and clears
the target later, but the server's accepted target persists until cleared. `SessionVoiceLinks.cs:39` projects
that target into the voice roster; `src/OpenScct.Net/Voice/VoiceRouting.cs:91` allows that speaker's audio when
the listener is alive and cross-team comms are enabled.

Keep voice-target eligibility on the server: verify active binocular/scope use, visibility/range and current
life state, and expire/revalidate the link. This also avoids a stale link after an interrupted scope exit.
The current code provides some range/team protection already; it should not be described as accepting every
target without checks. This is static review, not a demonstrated remote eavesdropping reproduction.

### N11 — P2: completed results are still mutable at the game-rule level

`sCheckEndGame` has no `bGameEnded` guard, and `Logout` calls it for a departing player outside the lobby.
`tests/OpenScct.Game.Tests/Game/EndOfRoundTests.cs:255` deliberately verifies that a spy victory can become a
timeout loss after the round ended. The online layer may have already captured its own result by then, which
creates two sources with potentially different answers. This is retained original behavior, not a Beta 5
change in the supplied `SGameInfo` delta.

Prefer an immutable completed result in the rewrite, while preserving the outcome ordering during a live
round. Verify result snapshots, replicated GRI values, match-history output and departure from the results
screen agree. The existing test codifies the old behavior and would need an intentional policy update.

### N12 — P2: rejoin grants a new pawn while retaining the old life count

`src/OpenScct.Server/Online/OnlineServer.Rejoin.cs:66` preserves score, kills, deaths and lives but not health,
remaining equipment, position or respawn deadline. Lines 112–130 restore those counters and call
`eventRestartPlayer` for a player with lives remaining. `ServerHost.cs:590` destroys the departing controller
and pawn. Consequently, current rejoin semantics allow a fresh spawn without spending a life; they also do
not preserve an in-progress respawn wait. The documentation explicitly describes a fresh spawn, so this is
an OpenSCCT policy/fairness tradeoff rather than a hidden implementation mismatch.

Decide whether competitive rejoin should restore surviving-pawn state, consume a life, or retain a defined
penalty/wait. Practice servers may reasonably use the current behavior. Validate against real disconnect,
rejoin during the same round, rejoin after a revenge round changed, eliminated players and expired sessions.

### N13 — P2: demo parsing needs resource and structural bounds

`src/OpenScct.Net/Demo/DemoFile.cs:208` sizes a memory buffer from file-provided index counters, and line 212
decompresses the whole packet stream without a maximum output budget. Index players/markers and strings are
also read without application-level bounds. Frame parsing at line 432 does not enforce the network datagram
maximum; `DemoPlaybackTransport.cs:44` later copies that payload into the receiver's fixed buffer.

The normal recorder produces bounded packets and the recent playback tests pass. Those facts do not validate
truncated, malformed, oversized or highly compressible imported recordings. Add checked counts/lengths/tick
ordering, a maximum decompressed size and a per-packet bound before playback. The library's handled-error
filter (`game/Scripts/Play/Demo/DemoLibrary.cs:118`) should report invalid recordings cleanly. These are static
robustness gaps; this audit did not construct harmful files or perform resource-exhaustion tests.

### N14 — P2: demo first-person view reconstructs the eye instead of recording the player's camera

Beta 5 adds view-update/relay declarations carrying timestamp, view/aim, camera XYZ and orientation, flags,
FOV, breath, accuracy and laser target, with native handlers 1917 and 1919. This establishes a richer
recording/spectating interface in the shipped scripts; it does not establish native runtime fidelity by itself.

OpenSCCT already has a demo browser, playback controls, weapons, multiple cameras and independent recording.
The remaining gap is exact viewpoint data. `game/Scripts/Play/Demo/DemoPlayer.cs:434` computes a watched
first-person view from pawn location plus eye height, packed proxy aim and the playback client's calculated
FOV (or 85). It does not consume the watched player's recorded camera position, camera roll or FOV. The
recording is an observer connection (`src/OpenScct.Server/Demo/DemoRecorder.cs:8`), and other players'
controllers are excluded from recorder relevancy (`src/OpenScct.Net/Replication/NetRelevancy.cs:113`). The
inspected pawn replication frame has motion/aim, animation, equipment and body state but no per-player
camera-position/FOV/breath/accuracy stream (`PawnStateReplication.cs:156`; `ProxyMotion.cs:11`).

A compact timestamped view stream for demos and authorized spectators would improve scope zoom, lean/head
offsets, recoil/camera motion and spy-camera reconstruction. Keep it separate from gameplay authority and
restrict who receives private view state. Validate by comparing an owner's rendered camera with the same
recorded tick across scoped/unscoped aim, traversal, lean, recoil, death, respawn and seeking. The need is
confirmed by current playback code; the visual magnitude and Beta 5 native behavior remain unmeasured.

## Checks executed

All commands ran on 2026-11-03 against the current checkout, after coordinating with the gameplay audit to
avoid shared build-output collisions. No tests were changed to obtain these results.

| Command scope | Result |
|---|---|
| `dotnet test --project tests/OpenScct.Game.Tests/OpenScct.Game.Tests.csproj --no-restore -- --filter-namespace OpenScct.Game.Tests.Game --filter-namespace OpenScct.Game.Tests.Objectives --filter-namespace OpenScct.Game.Tests.Devices --filter-namespace OpenScct.Game.Tests.Net` | 567 passed, 0 failed, 0 skipped. |
| `dotnet test --project tests/OpenScct.Net.Tests/OpenScct.Net.Tests.csproj --no-restore` | 214 passed, 0 failed, 0 skipped. |
| Integration project, exact class filters `TerminalHackNetTests`, `MatchClockNetTests`, `SpectatorNetworkTests`, `LobbyVoiceTests`, `Online.PersonalDemoTests` under `OpenScct.Integration.Tests` | 26 passed, 0 failed, 0 skipped. |

Total in this workstream: **807 passing tests, no skips**. The focused integration run took approximately
38 seconds and exercised the repository's real client/server test harness, including installed map content.
This is not a live internet multiplayer, microphone/audio, original-runtime or full demo-library acceptance run.

At the initial audit checkpoint no changes were made to production code. The review found actionable Beta 5 deltas even though the current
tests pass: some tests intentionally assert Beta 3 behavior, and the current positive-path suites do not cover
the defensive validation and input-boundary cases listed above.

## Authorized implementation follow-up

The subsequent implementation closes the last-team departure and completed-result gaps, separates decoy alarm
activation from real detection statistics, counts sticky-camera deployment, and fills the poison, shocked-merc
collision and posthumous explosive categories. New numeric counters are part of the career export. A direct
spy-bullet regression additionally found that it reused the camera's newly pawn-excluding trace flags; its trace
now includes pawns explicitly, with living/dead target coverage. Loadout snapshot/export work is owned by the
coordinating workstream.

Voice target acceptance now requires the proper scope state, living opponents, an allowed voice mode and an
authoritative first-hit trace. The relay revalidates these conditions without waiting for a client clear.
Competitive reconnects retain round counters but spend a living pawn's abandoned life and enter the normal
respawn wait; already-dead players pay no extra life. Practice retains immediate reconnect spawning. This is a
deliberate rewrite fairness policy, documented in `docs/ONLINE.md`, rather than asserted original-game behavior.

The demo parser now bounds metadata, strings, packet sizes/counts, expanded data, ticks and varints, without
allocating from claimed index totals. Container version 2 adds a separate bounded compressed player-view stream
while retaining version 1 parsing and the existing network protocol hash. Updated rendering clients supply actual
calc-view positions, rotation, FOV, aim, breath and accuracy at ten samples per second when a recording is active.
The server supplies identity, pawn and receive time; the data never changes gameplay or reaches other live players.
Playback interpolates samples, respects camera-source/body-visibility changes, and uses the legacy reconstruction
when samples are missing. This improves recorded POV fidelity, but receive-time sampling is not frame-perfect
capture. Older clients and headless bots supply no recorded-camera samples. Visual/audio acceptance of scope,
remote-camera and weapon overlays remains a manual check.

Regression coverage includes real local collision for voice, actual spy-bullet traces, poison and posthumous
explosions, decoy alarms, sticky camera deployment/gas, last-team logout and result immutability. The simulated
online harness covers practice/nonpractice reconnects, last-life exhaustion, respawn delay, retained statistics,
personal demo transfers and connection-associated camera capture. File fixtures cover bounded expansion,
metadata, malformed frames, view limits, seek interpolation, camera cuts and version 1 fallback. Final logs are
under ignored `local/b5-fixes/network/`; these checks do not replace original-runtime or live-player acceptance.

The independent review of the interaction changes also found a missing controller-phase check. Start and
completion now require the matching hack/hold workflow, preserving the legitimate remote Maintain Enter state
while rejecting completion from walking, menu or unrelated states. Cancellation still releases the owner's
reservation. The updated objective suite passes 185 tests; TerminalHackNet and MapInteractionParity pass all
seven integration cases with skipped tests treated as failures.

Final scoped verification: the complete Game suite passes 2,016 cases with five existing opt-in real-map AI
evaluations skipped; the complete Net suite passes all 240 cases. The combined reconnect, voice, demo,
terminal and map-interaction integration selection passes 21 cases with no skips. The latest integration
assembly was compiled against the refreshed Game/Net/Server artifacts and the previously built Master
artifact, because concurrent, unrelated progress-page edits temporarily prevented rebuilding Master. The
camera regressions also cover preserving the last view's timestamp when a packet arrives earlier and matching
the recorded pawn's network identity before applying a view after a respawn or seek.

## Rendering follow-up assigned during implementation

The additional B5 fog work now supports exact-class `SFogVolume` conversion,
convex brush pieces with the recovered sampled-box fallback, shared floor search,
open-face fade classification, height falloff, overlapping colours and zone
limits. Native height clipping affects culling bounds only; original shape planes
are retained. Portal extraction uses all distinct nonzero BSP side-zone boundaries,
including polygons without a Portal surface flag. The zero/negative fog interval
case now uses the native sharp-boundary inverse range instead of disabling fog.
The recovered behavior and evidence addresses are in
[`spec/native/enhanced-zone-fog.md`](../native/enhanced-zone-fog.md).

Shared independently authored fog equations serve both the material path and the
diagnostic compositor. Per-camera bindings preserve simultaneous main/sky/auxiliary
views, apply fog at each transparent surface's own depth, and skip duplicate post
fog and its unused SSBO upload. Registry storage now grows only when needed,
reuses bounded scratch, and clears retired slots and unused texture rows.

Verification passed 15 pure fog tests, seven converter tests, twelve Vulkan
volume phases with 96 off-center compute/material comparisons, and the cooperating
render workstream's 36 multi-view/MSAA surface samples. The surface and volume
fixtures also pass in the exported Windows debug runtime. The refreshed AquaE smoke
passed all 22 cameras and its night/thermal/EMF/motion/torch/laser/emitter checks.
Its median fog-frame build was 0.079 ms and median texture upload was 0.050 ms;
the reused upload allocated 376 managed bytes at an 80 KiB texture capacity.
Full measurements and their scope are retained in the fog spec and ignored logs.

Acceptance remains bounded: no supplied B5 map places an `SFogVolume`, so volume
coverage uses synthetic geometry and numeric pixel expectations. Manual original
runtime comparison is outstanding. Guarded pawn-trail shader code is statically
inactive in the shipped B5 setup and was not invented in the rewrite. Native
half-display-step fog dither is omitted while world blend-role selection remains
unverified in the retained CPU evidence. Diagnostic cameras using the compositor
fallback still inherit its opaque-depth transparency limitation; registered game
views use the surface path.
