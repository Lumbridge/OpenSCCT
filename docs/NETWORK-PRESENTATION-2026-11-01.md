# Network audio, crouch, death presentation and timers — 2026-11-01

Follow-up to [0.8.54](NETWORK-FOLLOWUP-2026-11-01.md), focused on the remaining movement audio,
crouch placement, death/spectator presentation and the subsequently reported countdown accuracy.
Validation and release work are in progress.

## Fixed

### Local movement sound routing

The original replaces a locally controlled pawn's one-shot sound source with its viewport controller,
clears the bone attachment and places the sound at the listener. The rewrite retained its foot/spine
position, so the third-person camera attenuated the player's own steps and rolls.

`SoundSystem` now binds those voices to the listener for their lifetime. `PlayAudio` identifies the
locally controlled pawn independently of the viewed/spectated actor. Remote sources keep their
world position, attenuation and occlusion. Original gains, radii, samples and ambient behavior remain.

Evidence:

- Exact retail instructions and retained editor decompilation agree on the local-controller predicate
  and listener relocation. The retail voice-array layout differs from the editor's; the first sampler
  attempt was rejected, not used as evidence.
- Fresh muted original runtime capture in the clear Steel Squat lane records **32 local movement
  voice observations**: 17 concrete footsteps and 15 `RollGearShort` observations. Every source is the
  viewport controller, bone -1, distance zero, while the pawn/camera separation is 251.8–262.5 uu.
- Six original WAVs used in the comparisons match the converted files byte for byte.
- The new actual-mixer regression fails before the fix at own-source distance 300 instead of zero.
  The fixed real mixer captures 55,296 stereo frames per case at 48 kHz, with zero discarded frames.
  Own near/far PCM energies and peaks are identical (amplitude ratio 1.000000); remote output at
  300 uu has ratio 0.17073171, matching the original sample's 28 uu radius and attenuation law.
- Real UDP audio tests pass in both role configurations, typical impairment at 144/30 FPS:
  `20261101-160030-19c18b88` (spy host) and `20261101-160348-29925887` (spy client). Natural
  footstep/roll events stay at distance zero/full source gain on their owner; the remote listener
  hears positional sources at approximately 169–1,056 uu with attenuation. These runs stay muted;
  actual output is proved separately by the mixer capture, not subjective listening.

Private artifacts: `local/original-presentation-audio-lane-20261101`,
`local/audio-source-parity-20261101.json`, `local/audio-retail-routing.rea.json`.
The original stayed muted; this is routing/sample evidence, not a recording of original loudness.

### Final-life spy death visible to other players

The spy's root-motion death action was consumed into a modern movement command after death/results
had disabled command simulation. The authority discarded that command, so the owner played its
death while observers continued to see crouched idle. Merc in-place deaths used a different action path.

`ActionsWaitForBigMove` now leaves actions for the reliable action RPC whenever modern command
simulation is disabled. Normal live movement still batches its actions as before.

The rendered before case `20261101-154516-d3c95901` shows `waitCrAlFd0` on the authority/observer
under the results overlay. After case `20261101-155121-bb9925ab` shows `XxxxDnAlBk0` on authority,
owner, guest and observer, with matching corpse positions within replication quantization.

The lab's final-life branch now checks the corpse before automatic results-to-lobby travel rather
than waiting through a respawn timeline that cannot apply to an ended match.

## Crouch: networking corrected, authored foot penetration still present

Fresh Aquarius real UDP test `20261101-155121-911c8a1c` uses a merc host at 144 FPS and spy client
at 30 FPS with the typical network fault profile. It has zero vertical body-anchor error, zero
missing-floor/unsupported samples, and animation in every moving observation window.

The strict 8 uu foot criterion **still fails**: observer -10.924 uu, owner -10.724 uu. Reconstructing
their exact submitted blends from original tracks reproduces both depths within 0.00004 uu. The
rendered BSP floor is 544.0 versus the collision surface at 544.5, the expected collision clearance.
The same method reconstructs the earlier Hub -16.45 uu sample. Idle and jog clips individually have
reasonable foot height; their articulated blend causes the penetration.

A fresh original capture contains actual crouched blends with a toe 15.016 uu below the collision
bottom. Applying the original skin weights/bind matrices to those captured bones projects the boot
18.439 uu below it. This is original-authored blend penetration, distinct from the previous network
stance-anchor bug. No global body offset or test-tolerance change was applied. Eliminating it would
require a deliberate foot-contact correction beyond original pose parity.

The new diagnostics retain exact drawn channels, foot world coordinates and body-anchor delta at
each extreme. They also correct a diagnostic Y-axis sign error and separate a historical extreme
from the later screenshot/current state.

The first fresh original pose replay is **525/531**, below the unchanged 99% threshold: five mismatched
frames match the preceding rendered pose; one is still 1.744 uu from that pose. Same TimeSeconds
does not prove channels and rendered SpaceBases belong to the same draw. This capture synchronization
limitation remains open; the smoke is not reported green. Existing retained captures still pass.

A second bounded capture waits for Present and requires stable tick, Present counter and repeated
bone reads. All 531 frames meet those guards (311 crouched, 220 standing, 70 rolling observations),
but replay remains **517/531**, worst 11.5 uu in roll recovery. That capture is retained separately;
the additional guards do not establish full pose parity. Six failed samples retain byte-identical
original bones across a new Present and tick, so this is not explained solely by torn memory reads.
Ten positive-frame outliers closely match the preceding sampled pose; three positive outliers and
one tween-history case need further investigation. No tolerance or failed frame was hidden.

The new landing maximum is 8.057 uu, including 1.969 uu of airborne interpolation. Fully grounded
maxima are 4.12 uu remote / 5.91 uu owner. The historical 59.38 uu sample lacks exact metadata and
remains unclassified. The original screenshot is not an exact worst-frame pixel comparison; the
penetration conclusion comes from bone/skin measurements.

Private artifacts: `local/pose-investigation-20261101`, `local/original-presentation-20261101`,
and the fresh network run above.

## Death comparison and network matrix

Fresh original Steel Squat combat fixtures use normal rifle fire, not a health-zero/state assignment:

- Crouched spy: `XxxxCrAlBk0` then `XxxxDnAlBk0`, translation root lock during the move.
- Standing merc: `XxxxStAlBk0`; the non-viewport target remains on that clip through the capture.
- Original authored assets/root translation match every bone over 330 non-tween death frames per
  role in the independent comparison: worst 0.34 uu spy / 0.33 uu merc (43 spy bones, 42 merc bones).

### Preserve original animation key timing

The actual Godot replay initially disagreed with otherwise correct converted assets: only 321/330 spy
and 327/330 merc death frames met the unchanged 1.5 uu limit, with errors up to 8.484 uu. Inspection
proved that the default importer rebaked an 8 Hz, 21-key death clip into 76 keys at 30 Hz. That misses
sharp authored changes between the bake samples.

Converted UE clips now retain their exact original LINEAR key times, with validated bone targets,
values and quaternion interpolation. Other imported/authored models keep their previous path.
The scan covered all 60 converted animated meshes and 47,000 channels. Actual `ChannelPoseApplier`
replay now passes **330/330 frames for each role**, worst 0.340 uu spy / 0.329 uu merc, including
translation root locks and the spy's resting corpse clip. No animation tolerance was relaxed.

These original captures use two characters in one original process, not original network transport.
The rewrite's separate processes use actual UDP. Original team tips on the spectator page are
intentional and remain; a combat tip there is not evidence of a broken spectator transition.

| Rendered real UDP case | Evidence | Result |
|---|---|---|
| Spy host, final life, crouched, LAN profile | `20261101-155121-bb9925ab` | Pass: all copies reach same corpse clip; exhausted team loses |
| Merc client, final life, standing, typical profile | `20261101-155513-e8838f45` | Pass: death and results agree |
| Spy client, final life, crouched, one spy teammate, rough profile | `20261101-155513-66f0bda0` | Pass: match continues, zero lives, watches living teammate, corpse error 0 uu |
| Isolated exact-key build, final-life crouched spy, typical profile | `20261101-160455-566020a9` | Pass: same death/result assertions after renderer correction |
| Before delayed-health fix: airborne merc client, spare life, rough profile | `20261101-160455-fcce0d72` | Fail: settled owner corpse differs by 43.084 uu |
| After delayed-health fix: same rendered rough-network case | `20261101-161628-91e96321` | Pass: corpse error 0 uu, living teammate spectated, fresh armed respawn |

The airborne case exposed another delivery-order race: inert landing can arrive before the separately
replicated health value. The owner advanced animation but skipped early corpse physics, losing root
displacement. An explicit dead/inert-death predicate now selects corpse physics immediately and is
used consistently for command admission and prediction completion. Healthy knockout states remain
on their existing path. A connected delayed-health regression fails before the fix at 59.22 uu spy /
48.94 uu merc and passes for both afterward, without teleporting bodies or changing timers.

The final connected selection passes 35 cases: both roles, both stances, five ordinary hit
directions on the final life, spare-life deaths, and both same-tick lethal-hit orders. The first
delivered lethal hit ends the match immediately; later damage respects the ended-game state,
matching the original rule rather than inventing a draw.
It also includes four airborne health-order controls/regressions and five existing KO, armed-jump
and results-braking checks. The actual rendered `play-anim` and `sidearm-reload-pose` checks pass:
four character rigs, rifle fire/reload/jump/landing and all sidearm grip/reload blends were exercised.
These are not a complete clip catalogue. Initial isolated runs used fallback UI fonts; the subsequent
headless import restored the font cache before the final airborne rerun.

The full suite exposed a separate fixture problem: the real UDP server-list test polled at up to
60 HTTP requests/s against a 240/minute per-IP production limit, starving its own roster heartbeat.
Its listing helper now waits at least 500 ms between requests while continuing simulation ticks.
The previously failing case passes in isolation (34.7 s). Production limits are unchanged; the
initial full-suite failure remains recorded rather than being called a clean pass.
That initial shared-checkout suite completed in 18m47s: 3,129 passed, 140 skipped, one failed out
of 3,270. It included concurrent work; the release is built and verified from a scoped clean checkout.

The clean presentation commit `d01357a` subsequently passed the full local suite: **3,089 passed,
141 skipped, zero failures**, 3,230 total in 16m48s. Its Windows, Linux and Godot CI jobs also passed
([run 41325910091](https://github.com/Lumbridge/OpenSCCT/actions/runs/41325910091)); each .NET CI
platform reported 2,711 passed and 519 skipped without private converted content. Linux packaging
passed separately. These results predate the countdown follow-up below and do not certify its final
release commit.

## Countdown accuracy

The visible round and spectator clocks now use `max(0, TimeLimit * 60 - ElapsedTime)`, the same
replicated integer clock and deadline used by the authority's time-limit outcome. This deliberately
removes the original page's extra one-second display subtraction: a round with one second remaining
must not already show zero. An expired finite spectator clock shows `0:00`; `Inf.` means unlimited.

Respawn and knockout labels use the ceiling of their own local deadline minus the matching local
clock. Exact ten-second respawns no longer initially show eleven, and a final positive fraction
still shows one. Alarm countdowns use the same precise match clock that established their deadline.
Countdown formatting carries minutes (`90` seconds is `1:30`). Gameplay durations are unchanged.

The connected HUD check uses a simulated 200 ms link with jitter and packet loss, ordinary lethal
damage, spectator transition and actual respawn. It observes all 61 round values from `1:00` to
`0:00`, checks 589 respawn samples within one displayed second of the authority deadline, and
records the first actual authority timeout at elapsed **60**. Evidence:
`local/timer-audit/hud-network-final.xml`. This is a connected two-world test, not a physical LAN run.

The wider inventory also traced camera-disabled, Hub queue, lobby launch and video-settings revert
countdowns. Those already use authority remaining durations or matching local deadlines with upward
rounding; no new defect was established there. Gas-mask and scoped-breath gauges read their actual
air/breath budgets. EMP, flash and grenade effects have no numeric remaining-time label.

The client GRI never runs the authority's one-second timer. Its unreplicated fractional-clock anchor
was left at spawn time, so combining it with replicated elapsed seconds counted client uptime twice.
The receive path now rebases that anchor on a changed elapsed value or match start, including a
zero-to-zero start. Unrelated property updates preserve its phase. Wire fields and gameplay timers
are unchanged. Connected clock tests fail twice before this fix and pass twice afterward, covering
35 seconds before joining, 45 seconds of connected sampling, jitter, unrelated same-second updates
and reset. Evidence: `local/match-clock-before.log` and `local/match-clock-after.log`.

Hack and objective countdowns now subtract that fractional match clock and round once. The delayed
Polar Base baseline showed **6 seconds with 4.9 seconds actually left**, then 2 with only 0.65 left.
Fresh, delayed-start (35.25 seconds) and interrupted/retried rendered solo checks now pass strict
comparisons with the actual authored five-second door deadline. Each observes `5,4,3,2,1`, finishes
at 5.067 seconds of sampled authority time, and removes the countdown on completion. The rendered
screenshots visibly show `0:03` and `0:01`. Evidence: valid fresh/delayed entries in
`local/door-timer-after/results.xml`, and `local/door-timer-retry-final/results.xml`. Earlier retry
harness failures are retained: its control branch and sample-reset assertion were corrected; they
were not production timer failures.

The separate regular-session UDP test then reproduced the user's **exact constant-zero** symptom
(`20261101-164315-64d96993`). Normal Use input entered the hacking state for 44 samples while the
authority deadline became **-3.43** and the owner HUD stayed zero. Gameplay still completed the
five-second hack. The deadline writer was subtracting the server's match-clock anchor from a
temporarily installed client input timestamp during command simulation, rather than world time.
Both precise-clock helpers and the GRI's anchor/reset/receive paths now use `WorldTimeSeconds`,
which remains stable inside nested movement-command scopes. The same real UDP host-spy case
passes after the fix (`20261101-164747-e26cdffc`), with positive countdown values and normal
completion. The earlier inline solo passes alone would not have established that fix.

The reversed-role rendered UDP case also passes (`20261101-164826-edda9531`, spy on the remote
client). Both cases use normal Use input after a validated placement fixture, show `5,4,3,2,1`,
complete the hack and remove its countdown. Under the typical impaired-network profile, displayed
time stays within one second of the authority deadline. Both screens were captured and inspected.
The final combined HUD and connected round/respawn checks pass after the world-clock correction:
`local/timer-audit/command-clock-hud-final.xml`.

Two focused command-clock regressions fail before the change (a valid precise time of 20.25 becomes
-14.2496; an intended hack deadline of 25.25 becomes -8.2496). Afterward, 98 focused game checks
pass, covering clock reset, the actual five-second switch lifecycle, terminal/objective interactions
and alarms. Three connected clock/terminal cases also pass. Evidence:
`local/command-clock-before.log`, `local/command-clock-after.log`, and
`local/command-clock-connected-after.log`. The baseline lab runner initially called the negative
deadline "not started"; its retained 44 active-hack samples demonstrate the defect. The runner now
recognizes any nonzero deadline and explicitly rejects an already-expired deadline early in hacking.

The follow-up call-path audit found that terminal hack start/stop already use world event time;
KO deadlines are established on the owning client; death/respawn transitions bypass deferred
movement commands; gas-mask air is a world-tick budget; and camera damage already protects its
disable deadline. No additional gameplay-duration changes were justified in those paths.

The final full HUD smoke passes every phase, including fractional alarm/knockout values, exact
round/respawn boundaries, spectator expiry and minute carry:
`local/timer-audit/hud-boundaries-final.xml`. An earlier run stopped at its first old hack-countdown
expectation, so that earlier run did not exercise the later boundary checks.

## Validation boundaries and remaining work

- These are separate rendered processes on this PC. Physical laptop/PC Wi-Fi behavior is not certified.
- Original-authoring foot penetration and the strict fresh pose-capture synchronization failure remain.
- All death clips on slopes, stairs and moving platforms, original KO/neck-break transitions and
  every spectator/attachment permutation are not yet exhaustively compared.
- The broader four-player limited-network input queue result from the overnight report remains outside
  this focused presentation fix.
- Concurrent AI/physics/traversal and deployment-directory work in the shared checkout is outside this
  change. Release staging must include only the files and hunks owned by this presentation work.
