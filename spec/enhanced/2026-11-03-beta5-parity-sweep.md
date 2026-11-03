# Enhanced SCCT 4.0 Beta 5 parity sweep

Implementation follow-up: [fixes, verification and remaining acceptance boundaries](2026-11-03-beta5-fixes.md).
The findings and test counts below describe the original audit snapshot, before those fixes.

Date: 2026-11-03. Rewrite: **0.8.74**, commit `12666be6fbfeeb59c28358d3d9337965ca315fc1`.
This is an investigation and implementation backlog. No gameplay, generated metadata, reference install,
content pack, release or live service was changed.

Beta 5 contains worthwhile fixes that the rewrite still lacks. The clearest gameplay benefits are the
last-player-disconnect round fix, interaction-menu input fixes and laser-mine arming/visibility fixes.
The broader review also found authority-validation gaps, approximate animated hitboxes, incomplete demo POV
capture, frozen animated materials and weaknesses in the parity verification gate.

## Scope and evidence

Inputs were the owner's two local installations:

- `C:\Users\ryans\Desktop\Enhanced SCCT Versus v4.0 Public Beta 3`
- `C:\Users\ryans\Desktop\Enhanced SCCT Versus 4.0 Beta 5`

Every baseline file represented in `re/binaries.lock.json` matched the supplied Beta 3 copy. This is a
**Beta 3 to Beta 5 comparison**; no Beta 4 copy was supplied, so changes cannot be attributed specifically
to the Beta 4 to Beta 5 interval. The public [Enhanced release listing](https://github.com/Joshhhuaaa/EnhancedSCCTVersus/releases)
examined during the audit still exposed 3.6 as its latest release. The local shipped files, rather than
public release-note assumptions, are the evidence for the beta changes below.

| Area | Work completed | Boundary |
|---|---|---|
| Entire installation | SHA-256 and size comparison of every file in both trees | Observed local copies; not remote release authenticity verification |
| Unreal packages | All eight `.u` packages decoded, including Editor, SoftBody and UnrealEd; all emitted classes compared | Static shipped-script/declaration/default behavior; native implementations are separate |
| Bytecode fidelity | Both token encoding and structured lowering round-trip checks passed for all 7,022 B5 script units, zero load errors | Establishes decoder fidelity, not runtime behavior or rewrite equivalence |
| Native patch | PE section comparison, ASCII printable-run delta (at least six bytes), focused Ghidra inspection of the B5 fog renderer | Other string encodings are outside that raw scan; not a whole-program semantic diff of Reloaded.Core or Reloaded.Editor |
| Rewrite gameplay | Current movement, camera, combat, equipment and physics inspected and tested; small local simulation probes | Original B5 gameplay was not launched |
| Rewrite online | Rounds, objectives, devices, RPC dispatch, stats, spectators, voice, rejoin and demos inspected; focused client/server tests | No public multiplayer/authentication or internet-latency acceptance run |
| Content/UI/audio | Map source/property/event comparison, repo and installed map-loader checks, menu and presentation code review | Structural content equivalence does not certify rendered/audio or interactive-event equivalence |

Detailed workstreams:

- [Gameplay, camera, equipment and physics](2026-11-03-gameplay-audit.md)
- [Rounds, objectives, networking, stats and demos](2026-11-03-network-objectives-audit.md)
- [Content, menus, rendering and audio](2026-11-03-content-ui-audit.md)

These distinguish decoded facts, current-code observations, local reproductions and unresolved runtime
questions. P1 means the next correctness work; P2 means a useful follow-up or a validation investigation.
Priorities express this audit's recommendation, not an upstream release severity.

## What changed in the supplied build

| Tree comparison | Result |
|---|---:|
| Beta 3 files | 628 |
| Beta 5 files | 629 |
| Byte-identical files | 615 |
| Changed files | 13 |
| Added files | 1 |
| Removed files | 0 |

The changed files are `Reloaded.Core.dll`, `Reloaded.Editor.dll`, `Reloaded_Editor.exe`,
`D3DCompiler_47.dll`, all eight `_PC_/*.u` packages, and `GUI.int`. The added file is
`System/Reloaded.Update.ex`. The original game image `System/SCCT Versus`, game launcher and
`ChaosTheory_Editor.exe` are unchanged. Every file under `Packages` and `Menus`, and the shipped INI files,
is byte-identical. Consequently this build supplies no changed maps, meshes, texture packages, sound packages,
map-settings packages or Magma menu files relative to the supplied Beta 3.

All package hashes changed, but their decoded changes are concentrated:

- Classes: **693 to 696**. Twenty existing class files change; three new classes are `GUI.Demo_Browser`,
  `GUI.ResignValidation` and `SBase.SFogVolume`.
- Functions: **6,073 to 6,102**, with **29 added**, **29 changed emitted function texts**, and none removed.
  This count includes declared native functions, and differs from the script-unit round-trip count, which
  includes state/replication scripts. Text changes are a review index, not proof of behavioral significance.
- Core, Editor, IpDrv, SoftBody and UnrealEd have no emitted class-text changes. Package-byte changes alone
  must not be reported as new gameplay in those packages.

Selected B5 identities (SHA-256):

| File | SHA-256 |
|---|---|
| `Reloaded.Core.dll` | `8194394290b42e555966b5fceef88dca035c0c41eaca2655c4f6033a2d690e87` |
| `_PC_/SBase.u` | `1779e3ba6f01d82c97ae58d3f4e7f73e551d2c4266899ac3655ff3ab95721783` |
| `_PC_/GUI.u` | `9cfa50514d1992aff299b777378046b21e8bb3217f8acb3d9f6980499b6c92c4` |
| `System/SCCT Versus` | `a98873aaec1de21fdcccc0afb9985e3ddcf38a9ddce1207214872d6428ec9950` |

The existing baseline lock and provenance attributes remain B3-based. Updating them without reconciling the
changed functions, defaults, stats schemas and scenarios would incorrectly imply that B5 behavior was ported.

## Recommended Beta 5 adoptions

| ID | Priority | Finding and benefit | Current evidence / acceptance check |
|---|---|---|---|
| B5-01 | P1 | End the network round when the final participant on one side quits. | `SGameInfo.EndGame.cs:43,98` excludes quitters then requires a remaining participant. `EndOfRoundTests.cs:117` explicitly enshrines the old behavior. B5 adds per-team quitter flags. Check each team, spectators, simultaneous departures, revenge rounds and one-time result publication. |
| B5-02 | P1 | Permit the interaction menu to open while moving, and preserve held up/down state when entering all three selection menus. | `Objectives/InteractionMenuRules.cs:48` retains the input-speed gate B5 removes. `SPlayerController.Interaction.cs:127`, `s_PutMineOnWall.cs:178` and `s_StickyCam.cs:322` reset previous keys to zero. The local probe confirms held keys become false new press edges. Test hold-forward/use, hold-backward, mouse wheel and a fresh key press after entry. |
| B5-03 | P1 | Wait for a valid opposite surface before arming a laser mine; hide an unsupported overlength beam. | Rewrite arms without a wall and retains an unhidden 2000.1-uu beam actor in the simulation probe. B5 retries initial arming every 0.25 seconds and updates active beam visibility. Check moving doors, chaff, rearming and normal mines separately. |
| B5-04 | P2 | Clear the controller's choking sound in the gas-mask server stop/other path. | `SGadGasMask.cs:75` clears only the pawn there. Probe confirms controller sound remains set. Local toggle cleanup can conceal the problem. Verify a remote owner across depletion, deselection and knockout. |
| B5-05 | P2 | Prevent simulated decoy alarms from incrementing real detection statistics. | `SAlarm.s_Listening.cs:42` routes simulated activation through ordinary Trigger/CountSpyAlarm. B5 retains alarm effects without the real-detection increment. Test the listening state and preserve other alarm-state rules. |
| B5-06 | P2 | Count sticky-camera use at successful deployment, with gas activation tracked separately if wanted. | Rewrite increments `Spy_StickyCamUsed` on gas release. B5 moves it to `SGadStickyCam.SpawnCam`. Current exports feed `spy.sticky_cam_uses`; historical totals need a deliberate interpretation. |
| B5-07 | P2 | Fill the remaining detailed stats: poison-mine deaths, sticky-blast victim deaths, ammo-stock uses, spy-bullet targets and loadout metadata. | Ordinary hip-fire, sniper, termination and explosive kill attribution already exists in `SGameInfo.Feats.cs`. Add only missing cases. Also check posthumous explosive kills after the killer's pawn is gone and clear loadout metadata on spawn/gear change. |
| B5-08 | P1 investigation | Retain authoritative selected-gadget and laser-target/dwell state. | B5 adds selected-gadget/native and laser-target server messages. Rewrite local selection/laser tick is not the same as an authority observation. The local laser receiver accepts an unearned mark; see R-01. Native behavior behind the B5 hooks remains partially unresolved. |
| B5-09 | P2 | Clear residual view roll in spy camera updates and the grabbing-player view. | B5 adds roll reset in both methods. `SPlayerController.Movement.Rotation.cs:78` and `CombatGrabStates.cs:669` preserve it. Probe seeded with roll 2048 retains 2048 after both updates. This proves the state mismatch; the normal-play path that leaves such a roll needs capture. Preserve deliberate camera shake separately. |
| B5-10 | P2 | Expose Resign with confirmation and prevent an unready client becoming Ready without its map. | Rewrite already has the resign backend and launch-time map rejection, but lacks these UI guards. This is an accessibility/consistency improvement, not proof invalid-map matches can launch. |
| B5-11 | P2 | Record enough per-player view state for faithful demo POVs. | B5 adds relays for camera position, view/aim, FOV, breath, accuracy, laser target, gadget state, gun channels and stats. The rewrite already records network demos and supports a library, seeking and first-person weapons. Its following views reconstruct eye/aim and use current calculated FOV, so exact recorded camera/zoom fidelity is a separate gap. |
| B5-12 | P2 investigation | Evaluate the new native zone/volume fog rendering and support `SFogVolume` if future maps need it. | Native code reads the new five-property fog-volume class; rewrite only has existing zone-distance fog. No supplied B5 map uses the new class. Compare doorway/portal fog and existing fogged zones before choosing a renderer change. |

For B5-02, the old speed restriction is expressly tested at
`tests/OpenScct.Game.Tests/Objectives/InteractionSystemTests.cs:422`. Passing that test means agreement with
the B3 rule. Remove the gate and change the test together when adopting B5. The three menu constructors
must seed previous input from current input; the ordinary subsequent release/press and wheel behavior stays
important to test. The local probe records current held keys `1,1` and previous keys `0,0` for all three states.

For B5-11, new RPC names do not imply the rewrite needs the same wire protocol. The useful requirement is
recorded output fidelity and server-owned state. Preserve the rewrite's explicit pawn animation stream and
scoped damage attribution. Enhanced and OpenSCCT demo-file compatibility was not established; a shared magic
string is not a format or semantic compatibility guarantee.

## Broader rewrite gaps

| ID | Priority | Finding | Confidence and next step |
|---|---|---|---|
| R-01 | P1 | Merc laser mark requests lack authoritative beam, target, range, visibility and dwell validation. | Locally reproduced through the owned controller's combat receiver: an off laser with no tracked target marks a living spy 50,000 uu away. This is a receiver-path proof, not a public-server test. Validate the earned mark on authority. |
| R-02 | P1 | Interaction and gameplay-object damage RPCs validate receiving-controller ownership but not the full supplied action/target. | Static review traces generated layouts through dispatch to `InteractionMenuRules` and `SPlayerController.Devices.DestroyGPO`. Require authoritative valid interaction/attack state; remote wire reachability was not demonstrated. |
| R-03 | P1 | Voice target selection lacks full authority-side binocular/scope, line-of-sight and continuing-validity checks. | Static review: some team/range checks exist, but accepted links persist until cleared. Add server eligibility/revalidation; actual remote misuse was not tested. |
| R-04 | P1 investigation | Most combat hitboxes do not follow visible animation. | Live `RollPillPose` samples supported spy rolls; non-roll spies and all mercs fall back to a scaled reference stance. Compare hanging, split-jump, leaning, KO and recoil poses before choosing changes. This is a confirmed implementation limitation with unmeasured mismatch severity. |
| R-05 | P2 | Completed game-rule outcomes remain mutable. | Existing end-round test explicitly expects a prior spy win to turn into a timeout loss when rechecked. Make completed results immutable and compare game rules, GRI, published result and match history. |
| R-06 | P2 policy | Rejoining gives a fresh pawn without spending a life or preserving remaining health/equipment/respawn wait. | Confirmed current documented policy, not accidental B5 divergence. Decide the competitive rule before changing it; retain practice flexibility. |
| R-07 | P2 | Imported demo parsing lacks application-level resource and structure bounds. | Static review of whole-stream decompression, index counts and packet lengths. Add checked bounds and clear invalid-file reporting; no resource-exhaustion test ran. |
| R-08 | P2 | General MaterialSequence playback is frozen at its first usable item. | Current converter/renderer code plus 32 sequence graph nodes on 11 maps. Compare full monitor/alert cycles, not screenshots. |
| R-09 | P2 | The installed content set misses nine moon sprites. | Repo conversions contain them; this machine's installed 81-map set lacks both metadata and runtime texture fallback for nine maps. Establish pack version/update selection before regenerating. Latest published packs were not checked. |
| R-10 | P2 investigation | Climb input uses a fixed screen-up approximation; presence detectors keep two tracked-spy slots; audio allocation lacks the occluded-zone priority-radius correction. | Confirmed implementation limits; assess camera angles, larger matches and voice contention respectively. These are not newly changed B5 behaviors. |
| R-11 | P1 verification | The parity evidence is much narrower than green unit-test totals suggest. | Missing golden/content fixtures silently return success; the fall-ledge golden intentionally stops before a surviving discrepancy; existing B3 tests reject several desirable B5 behaviors. Make a strict, fixture-aware parity gate. |

The gameplay and network reports contain concrete source locations and acceptance conditions for R-01 through
R-07 and R-10/R-11. Some issues are inherited original behavior or documented rewrite policy. They should not
be silently changed as if every difference were an implementation error. Retain intentional additions such
as practice rules, innate cloak, sidearms, optional perspective and removal of the shocker draw snap.

## Native findings and limits

The B5 Reloaded.Core code and data sections changed; Reloaded.Editor also changed. `Reloaded_Editor.exe`'s
`.text` is unchanged and its `.rdata` differs. The new updater and changed shader compiler are distribution
infrastructure, not gameplay rules to transplant into the Godot rewrite.

The local native string delta exposes demo recording/retention controls, playback cards/free-camera controls,
zone-fog shader resources and D3D11-layer identifiers. These are leads, not proof that every path executes.
Focused Ghidra 12.1.4 analysis adds stronger static evidence:

- At `0x1017c650`, the patch looks up `SFogVolume` and its `FogColor`, `FogDistance`, `HeightFalloff`,
  `EdgeFade` and `bLimitToZone` fields, iterates matching actors, rejects nonpositive fog distance, and has
  brush/box handling and failure paths. Evidence
  `ev_dcbb0648ec1c6da64c5958f3325fb2d4a9cb5c252bc0171072d141652ff36b8c`.
- At `0x1016d240`, it creates fog-pass resources and compiles the depth/reach shader variants with failure
  checks. Evidence `ev_7c38b81634a22789afc19a0714b028625b2d2dd870eccceee953dbd726ff0748`.
- Seed string evidence is `ev_f4a2aaf85782b8d38e890a553a17544a2e0189dc193548ee835f9944ccd7ba5e`.
  The evidence bundle retains cross-references and containing-function bounds as well.

This establishes real native fog implementation beyond the new script declaration. It does not establish
rendered fidelity, performance, which renderer backend was selected at runtime, or all changed native fixes.
The B3 native DLL was not fully decompiled and semantically matched against B5. Runtime-selected memory hooks,
frame-rate fixes, lag compensation, precise new native relay semantics and editor-only changes remain partial.
The native analysis session was exported and closed cleanly; raw pseudocode stays in ignored local storage.

## Verification results

| Check | Result |
|---|---|
| B3 all-package decoder round trips | 6,993/6,993 T1 and T2; zero load errors |
| B5 all-package decoder round trips | 7,022/7,022 T1 and T2; zero load errors |
| Physics | 203 passed |
| Game movement/combat/equipment/camera | 1,002 passed |
| Game lifecycle/objectives/devices/network | 567 passed |
| Network library | 214 passed |
| Focused client/server integration | 26 passed |
| Map-audit tool regression cases | 12 passed |
| Repository map loader | Passed across 85 maps |
| Installed map loader | Passed across 81 maps |
| B5 source versus repository map conversion | 83 matching source packages; 28,718 actors and 8,701 edges across the 85-map inventory; zero missing/extra actors, property differences, decode errors or structural issues |
| Current baseline coverage report | Zero errors, 63 warnings; 168 units verified by the existing differential evidence |
| Whole fall-ledge recording beyond current cutoff | Still diverges: 183 differences, first at frame 177, approximately 0.69 uu in Z |
| Local behavior probes | Reproduced retained gas loop, unsupported armed laser beam, unearned laser mark, retained camera roll and menu input-edge reset |

The five gameplay/physics/network/integration rows total **2,012 passing test cases**, with zero reported
skips. The initial concurrent build collision was resolved by sequential test builds; it was not a product
failure. The independent map-loader runs cover different installed/repository data sets.

MoonD and TestRange have no matching B5 source package in the map comparison. The audit retained 231 unmatched
event names and five render-only targets also present in the source; these are candidates for interactive
review, not proof of missing gameplay. The installed sprite metadata issue demonstrates why a successful
loader test is not sufficient visual acceptance.

The refreshed coverage tool still uses the B3 inventory: 7,412 units, 328 out of scope, 168 verified, and
**4.9% of in-scope bytecode weight backed by its differential verification gate**. This is neither a product
completion percentage nor proof the rest is broken. Independent replacement systems and provenance annotation
coverage affect its port counts. It does demonstrate that unit-test totals cannot justify a blanket parity claim.

## Implementation order and remaining original-runtime checks

1. **Correctness:** B5 last-team departure, menu input and laser-mine rules; authority validation for interactions,
   GPO damage, laser marking and voice links. Update tests that currently require B3 behavior, and verify legitimate
   remote play still works. Freeze completed results as a separate lifecycle decision.
2. **Small parity and stats fixes:** gas-mask cleanup, residual roll, decoy-alarm and sticky-camera counters,
   missing kill/death cases and stats metadata; resignation/ready UI. Avoid changing historical stats meaning
   without an explicit migration/display convention.
3. **Presentation and recordings:** faithful per-player camera/gadget/gun state for demos, bounded demo import,
   animated hit zones and material sequences, installed sprite-pack diagnosis, then zone/volume fog comparison.
4. **Strict parity evidence:** record B5 reference runs through the permitted original-game harness and compare
   the full trajectories. Keep reference game files read-only and independent of generated production metadata.

Required original-versus-rewrite acceptance matrix:

- 30/60/120/240 fps; host and remote client; latency/loss cases for moving actors, grenades, gadgets and view state.
- Doors under laser mines before and after arming; held movement on interaction/mine/camera menu entry;
  gas-mask depletion, selection change and knockout; real versus decoy alarm attribution.
- Last spy/merc departure, spectator departure, completed-result departure, revenge-round rejoin and respawn wait.
- Animated head/limb hit zones during lean, hanging, split jump, roll and KO; full fall-ledge collision trace;
  camera-dependent climbing directions and the remaining sticky-camera lighting/receiver conditions.
- Demos viewed from each player at scope zoom, recoil, breath, gadget switch and seek boundaries; malformed-file
  rejection. No Enhanced/OpenSCCT recording compatibility claim until an explicit format comparison exists.
- Full material cycles, fog across portals/zone boundaries, moon sprites, crowded-source audio/reverb and
  controller hardware. Static data audits and local simulations do not complete these checks.

## Complete decoded class-change index

| Package | Changed or added class files | Review disposition |
|---|---|---|
| Engine | `SPlayerStats` | Stats schema, B5-07 |
| GUI | `Controller_Settings`, `GUIController`, `GUIYesNoPage`, `moListEditBox` | Declaration/subobject order/name changes; no useful behavior established |
| GUI | `Demo_Browser`, `Menu_Multi`, `loading` | Demo menu/return routing; existing rewrite feature, B5-11 is the remaining fidelity question |
| GUI | `ResignValidation`, `SSMenu_LANMainPage` | B5-10 |
| GUI | `Lobby_Create` | Missing-map ready guard, B5-10 |
| SBase | `LancementMenu` | Demo return routing |
| SBase | `SAlarm` | B5-05 |
| SBase | `SBasePawn` | Selected gadget and inventory-stat state, B5-07/B5-08 |
| SBase | `SFogVolume` | B5-12 |
| SBase | `SGadGasMask` | B5-04 |
| SBase | `SGadStickyCam`, `SStickyCam` | B5-06 |
| SBase | `SGadTorcheLaser` | B5-08/R-01 |
| SBase | `SGameInfo` | B5-01/B5-07; existing scoped attribution retained |
| SBase | `SMineLaser` | B5-03 |
| SBase | `SPlayerController` | B5-02/B5-07/B5-09/B5-11, plus termination context |
| SBase | `SWeaponDefense` | Firing context; rewrite already has analogous attribution |

## Local evidence index

Everything below is under ignored `local/enhanced-b5-audit/` and should remain local under `re/PROVENANCE.md`:

- `tree-diff.json`: full paths, sizes and SHA-256 identities for both inputs, and changed/added/unchanged lists.
- `b3-uc/`, `b5-uc/`, `uc-diffs/`, `uc-diff-index.json`, `function-delta.json`: decoded comparison and indexes.
- `b3-all-package-check.json`, `b5-all-package-check.json`: all-eight-package decoder validation. The earlier
  `b5-bytecode-check.json` covers the default five gameplay packages only; use the all-package files for totals.
- `native-strings-delta.json`, individual native-string files and `pe-sections.json`: native discovery evidence.
- `rea-b5-native-evidence.json`, `rea-b5-native.snapshot.json`: exported REA/Ghidra observations and limitations.
- `gameplay-probe/`, `controller-probe/`: independently written local reproduction programs and results.
- `content-map-audit/`, `installed-content-audit/`, `installed-sprite-fallbacks.json`, `content-known-gaps.json`:
  structural and rendering metadata evidence.
- `coverage-current/`: refreshed baseline report, without changing tracked coverage files.

The sweep answers the full file/script-delta and current-source audit questions. Native semantic equivalence,
visual/audio parity, every map event chain and fresh B5 gameplay across the acceptance matrix remain explicitly
unverified. No report here should be read as certifying complete gameplay parity.
