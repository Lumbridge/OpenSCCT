# Enhanced Beta 5 gameplay parity audit

Audit date: 2026-11-03. Rewrite inspected: `12666be6fbfeeb59c28358d3d9337965ca315fc1`.
This contribution covers movement, camera/input, combat, equipment and physics. The findings below preserve the
initial audit; the implementation follow-up at the end records authorized fixes and current validation.
The parent sweep owns package identity, the full controller diff, objectives, networking,
content and UI. Reference-game files were only read. No public service or multiplayer session was contacted.

Evidence boundaries:

- Beta 3 versus Beta 5 script differences: private decoded diffs in
  `local/enhanced-b5-audit/uc-diffs/SBase`. These establish shipped script changes, not native execution.
- Current rewrite: source inspection, 1,205 passing automated cases, and the independent local simulation probe
  in `local/enhanced-b5-audit/gameplay-probe`. Its `results.txt` records results and reproduction commands.
- Existing original recordings were reused only for a movement regression check. No fresh Beta 5 original
  gameplay capture, visual/audio comparison, controller-hardware test or real multiplayer test ran in this scope.
- Existing `spec/enhanced/FEATURES.md` is an early inventory, not current implementation status. For example,
  cinematic pad controls are implemented in `game/Scripts/Options/GamepadInput.cs:251`, despite the older note.

## Beta 5 changes worth carrying forward

| Change | Current rewrite | Assessment and next check |
|---|---|---|
| Laser mines defer initial arming until an opposite surface exists, retrying every 0.25 seconds; active beam visibility follows whether length exceeds the supported maximum. | `SMineLaser.cs:54` inherits the unconditional arming timer in `SMines.cs:109`. `SMineLaser.cs:136` writes a maximum-plus-0.1 length when no wall exists, without hiding the beam; activated timer at `SMineLaser.cs:267` only checks presence. | **Confirmed missing Beta 5 behavior, P1.** Probe: after 5.3 seconds without a wall, rewrite is armed and has an unhidden 2000.1-uu beam actor. Check a door opening between placement and arming, opening after arming, then closing. Preserve normal chaff/rearming and poison-mine behavior. |
| Gas mask stop/other server sound path also clears a controller choking loop. | `SGadGasMask.cs:75` clears the pawn loop only. Local `Activate(false)` clears the controller too, so ordinary local toggling can conceal the server cleanup gap. | **Confirmed missing cleanup, P2.** Probe sets the choking loop on pawn and controller, invokes the server stop path, and leaves the controller loop set. Follow with a remote-owner test through the choking threshold, deselection, knockout and expiry; audio audibility itself was not measured. |
| Sticky-camera use statistics move from gas release to successful camera deployment. | `SGadStickyCam.cs:196` creates the camera and decrements its charge without increasing the counter. `SStickyCam.cs:168` still increases it when gas is released. | **Confirmed semantic mismatch, P2.** Adopt placement counting if the field means camera uses; retain a separate gas-release counter if useful. `OnlineServer.cs:1442` currently exports this field as `spy.sticky_cam_uses`, so dashboards and achievements inherit the old meaning. Do not silently relabel historical totals. |
| Client laser-target changes are sent to the server, which retains target and whole-second acquisition time. | `SGadTorcheLaser.cs:126` maintains the target locally; human-owned gadgets skip that tick on dedicated servers. There is no corresponding target-state RPC in `SPlayerController.CombatNet.cs`. | **Missing authority-side observation, plus a confirmed authority gap described below, P1.** The new RPC alone does not prove that Beta 5 validates the eventual mark; native hooks remain outside this contribution. Server-observed dwell should be the basis of acceptance, with latency tolerance and camera-network behavior checked. |
| Selected gadgets are sent from the pawn to a new native server handler (native ID 1918). | `PawnInventory.cs:220`, `:249`, and `:311` change local selection. `PlaySession.cs:454` executes selection commands on the client; the inspected combat receiver has no selected-gadget RPC. | **Useful synchronization candidate; native details unresolved.** Confirm authority/observer selection after slot keys, wheel, quick launch, loadout change and reconnect. Do not report an end-user failure solely from the absent RPC name: gadget-use requests can carry their own gadget reference. |
| Inventory grants record gadget classes and weapon class in player statistics. | `Loadouts.cs:206` and `:237` validate and grant equipment but do not fill equivalent fields; the current generated player-stat type does not declare the Beta 5 additions. | **Telemetry enhancement, P2.** Useful for per-round loadout history and balancing. Integrate deliberately with the rewrite's sidearms and mid-round gear changes; do not regenerate class metadata from Beta 5 without reconciling all consumers. |
| Rifle firing brackets its trace with a current-firing-weapon field on game info. | `SWeaponDefense.cs:377` already pushes a scoped `CombatDeathContext`, and `:437` retains `LastBulletWeapon` for victims. | **Already has an analogous provenance mechanism.** The Beta 5 field's downstream/native uses need the parent investigation. No missing combat behavior follows from the absence of this exact field. Preserve the rewrite's nested scoped attribution rather than adding a second global tracker without a demonstrated consumer. |

The mine regression test `MineTests.cs:173` currently requires a full-length beam without an opposite wall.
It passed in this audit and would need changing with the Beta 5 behavior. A green suite here establishes
consistency with the old test contract, not Beta 5 parity.

## Concrete rewrite issue exposed by the audit

### Merc laser marks trust the requesting client

**Priority P1; confirmed in the local simulation.** `SPlayerController.CombatNet.cs:168` accepts a mark request
for the player's owned laser gadget. `SGadTorcheLaser.cs:209` then checks only whether the requested target is
alive before applying the mark. There is no check here for an active beam, a valid enemy, line of sight, range,
or completed dwell. Owning the gadget is necessary but does not establish that a mark was earned.

The scratch probe sent this request through `ReceiveCombatCall(..., true)` in a dedicated-server world. The
laser was off, had no recorded target, and the spy was 50,000 uu away; the spy became marked. This is a receiver
and gameplay-path proof, not a network-packet or public-server test.

Suggested repair: retain authority-side target/dwell state, validate current living possession and enemy identity,
and reject unearned marks. Add legitimate continuous-dwell, interrupted-dwell, obscured-target, off-laser,
dead-owner, distant-target and camera-network checks. Keep the current three-to-four-second legitimate mark
timing covered by `MercLaserMarkTests`; do not add extra round-trip delay to the owner's visual beam.

## Broader parity gaps and verification limits

### Most animated hitboxes still use reference poses

**Priority P1 investigation; confirmed implementation limitation, outcome not yet measured against Beta 5.**
`src/OpenScct.Server/Maps/RollPillPose.cs:26` samples real animation only for supported spy roll clips. All merc
states and non-roll spy states use `EstimatedPillPose` at `src/OpenScct.Game/Combat/IHitZones.cs:162`, which
scales a reference stance by the collision cylinder and rotates it by yaw. It does not follow the visible bone
pose during back-to-wall lean, pipe hanging, split jump, climbing, knockout, recoil or most other animation.

This is the installed gameplay hit-zone provider on the server and client, not just a unit-test fallback:
`MapCatalog.Characters.cs:21`, `ServerHost.cs:228`, `ClientSession.cs:497`, and
`game/Scripts/Play/Session/PlayMapLoader.cs:48`. The recent roll work improves one important case but does not
make general skeletal hit zones complete. Prioritize deterministic authoritative pose sampling for the poses
whose visible head/limb locations depart furthest from the reference stance. Compare actual pill endpoints and
shots against original recordings before changing damage balance.

### Climbing input ignores the camera-dependent projected climb direction

**Priority P2; confirmed approximation, mismatch envelope unresolved.** `ControlInput.cs:168` and `:220`
interpret the climb direction as screen-up regardless of camera/pawn geometry. The source and
`spec/native/input.md:163` explicitly mark the original projection unresolved. Existing `SlowClimbTests` verify
forward/backward motion with the ordinary view, not different camera positions. Capture ladder, vertical pipe,
horizontal pipe and zip-line movement with side, front and steep camera angles, using both keys and analog input.

### The fall-ledge golden is intentionally cut off before a surviving discrepancy

**Priority P2 investigation; locally reproduced discrepancy.** `GoldenReplayTests.cs:34` stops
`movement.spy-fall-ledge` at frame 176. Invoking the same test through the retained local recording without that
cutoff produced 183 differences, beginning at frame 177 with about 0.69 uu of vertical position error. Subsequent
printed frames showed roughly 0.64 uu. The existing comment attributes this to converted collision, but the
current probe does not independently prove that cause or the eventual gameplay impact. Revisit the surface and
collision contact, compare the whole trajectory, and then remove or explicitly justify the cutoff.

The ten movement goldens and converted SteeE data are present on this machine. `GoldenReplayTests.cs:40` and
`:46` nevertheless silently return when either is absent on another machine, reporting a pass without comparisons.
Use an explicit skipped status for optional local fixtures and a separate strict parity gate that fails if a
required original recording/content fixture is missing. Do not equate aggregate unit-test totals with executed
original-game comparisons.

### Sticky-camera flight still has a renderer-dependent boundary

**Priority P2 visual validation; already documented residual.** The input-side extra solve and shadow eligibility
work are implemented, but `spec/native/camera.md:298` records additional original projector/frustum/receiver
conditions outside the rewrite's lighting eligibility model. Validate camera fly-in across lit/dark positions,
nearby and absent receivers, different FOVs, pipes and split jumps. Current static evidence does not justify a
new arbitrary speed multiplier.

### Presence detectors retain two tracked spy slots

**Priority P2 extended-player-count check; faithful old limit, not a proved Beta 5 regression.**
`SPresDetector.cs:113` and `:198` track only the first two qualifying spies. The third and later moving spies can
be absent from the per-spy detected notification while the detector is already warning. With supported 3v3 or
larger modes, verify the intended notification behavior and rotate/expand tracking if needed. Compare Beta 5
native behavior before claiming that its own two-slot implementation changed. A sound-play call remains in this
path, but that alone does not disprove Enhanced's silent detector behavior because the referenced audio asset
could itself be silent; no audio claim was made from the call name.

## Validation and implementation assumptions

- Physics suite: **203 passed, 0 failed, 0 skipped**.
- Game Movement, Combat, Equipment and Camera namespaces: **1,002 passed, 0 failed, 0 skipped**.
- Probe results: confirmed unearned laser mark, incomplete gas-loop cleanup, unhidden no-wall laser beam actor, and
  surviving full-length fall-ledge golden discrepancy.
- Initial concurrent build attempts collided on a shared DLL; sequential reruns above passed. That was a build
  orchestration problem, not a product failure.
- At the initial audit stage no production files were edited. This report and private probe can be
  removed independently. Future fixes should remain separate from generated metadata or wholesale content
  migration, so each behavior can be reviewed and reverted on its own.
- Before adopting Beta 5 rules, retain supported rewrite additions: innate cloak, optional perspective, sidearms,
  practice rules and the deliberately removed shocker draw snap. Their differences alone are not bugs.
- Automated cases do not establish rendered/audio parity, analog-controller feel, original native-hook behavior,
  or real latency behavior. Those are explicit remaining checks, not work silently counted as complete.

## Authorized implementation follow-up

The initial audit findings above are historical. The following changes are now implemented in the shared working
tree, without a commit or deployment:

- **B5-02/B5-09:** menu entry preserves held up/down edges, and spy camera/grab view rotation clears roll. The
  interaction movement gate is removed by the objective-validation slice. Tests cover all three menus and both
  held directions, plus camera/grab roll cleanup.
- **B5-03/B5-04:** laser mines wait for an opposite surface before arming and hide an over-range beam; gas-mask
  server stop clears a matching controller choke loop while preserving unrelated audio.
- **B5-08/R-01:** gadget selection sends the final owned selection once, and authority validates the current live
  pawn, inventory membership and allowed state. Laser acquisition and whole-second dwell now run on the server
  for human players too. Receipt retraces the current beam; inactive, unearned, interrupted, off-target and stale
  requests cannot mark. Continuous contact marks once. This is an authority improvement, not an exact port of
  Beta 5's client-written target-state RPC. Native handler 1918 remains unrecovered.
- **R-04:** both characters use installed animation tracks for hit zones in general gameplay, with channel masks,
  blends, mirrors, loop seams, negative-frame transitions, global start-bone transforms, root translation/yaw
  locks, full-spine aim and existing world-space bone controllers. History advances at simulation pose-cache
  points and pawn post-ticks, even with no shots and lag compensation disabled. Queries sample a copy, prediction
  replays cannot overwrite history, and network resets/time rewind clear it. Missing clips or incomplete skeletons
  still use the full reference-pose fallback. The mirror table uses the mesh skin's joint order, which includes
  helper bones absent from animation metadata. Renderer-only hub emote overlays are outside combat geometry.
- **R-10 input:** native assembly resolved the climb projection's integer absolute-value step: ordinary camera
  angles keep screen-up; the exactly vertical singular case uses the projected pawn-facing direction. The rewrite
  now follows that rule with normalization. This corrects the audit's overly broad implication that normal side
  camera angles should alter climb input. No new movement factor was introduced.
- **R-10 population:** presence detectors track every qualifying spy and notify each newly detected identity once,
  retaining the existing warning timeout. The two generated fields mirror the first two entries for compatibility.
  This intentionally extends the old two-spy limit for larger rewrite matches; it is not claimed as a Beta 5 change.

Validation after these changes and the parent collision fix:

- Game Equipment/Objectives/Camera/Combat/Movement namespaces: **1,247 passed**.
- Focused pose integration suites: **66 passed**, including first-shot-after-idle vs repeated queries, prediction
  replay isolation, network reset, synthetic root-unlock transitions, global masks and mesh-order mirrors.
- Additional prediction, lag-compensation, animation and goggle integration checks: **27 passed** from the final
  built test DLL. Rebuilding that extra run was temporarily blocked by unrelated concurrent Master UI edits;
  the DLL already included this slice's final code and tests.
- Godot build/import and extended `roll-collision` smoke passed: **6,615 collision-point comparisons**, maximum
  renderer disagreement **0.00015 uu**. This covers both installed characters, all 483 available clips in ordinary
  and mirrored form, plus sampled tweens, root translation/yaw locks and unlocks, global masks and bone controllers.
- Local content probe: **966 animated samples, no fallback and no non-finite/incomplete result** across both teams
  and mirror choices. All-clips loading took 0.62 seconds and retained about 76 MiB in this diagnostic process;
  these are one local measurement, not a performance guarantee.
- Scoped `git diff --check` passed. Reference data/decompilation remains under ignored local content paths.

The shared pose implementation is checked against the rewrite renderer, not shot-by-shot captures from the
original. Bone direction semantics retain the existing estimated native interpretation in `aim-ik.md`. Exact
vertical climb controls still need an original-runtime input comparison. Choke-loop audibility, camera fly-in
lighting/frustum rules, real network latency and larger-team play remain manual validation boundaries. Each
behavior is scoped to its owning classes/tests/specs and can be reverted independently; no asset migration,
generated class replacement, protocol rollout or public service action is part of this slice.
