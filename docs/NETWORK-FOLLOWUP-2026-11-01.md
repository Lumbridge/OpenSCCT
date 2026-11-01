# Spy-host / merc-client follow-up — 1 November 2026

**Released and deployed:** [OpenSCCT 0.8.54](https://github.com/Lumbridge/OpenSCCT/releases/tag/v0.8.54),
1 November 2026 at 14:34 UTC, from commit `5d516353e2c97d1628196717b22ec12b8c4324af`.
This follows Ryan's hands-on 0.8.53 report; it does not supersede the outstanding acceptance items
in the overnight report.

## Findings and changes

| Report | Cause / change | Evidence |
|---|---|---|
| No arms, rifle or grenades after grab KO | Merc body-animation draw/holster handlers were missing. The grab set `WeaponLoaded=0`, but the authored wake notify never restored 2. Registered the merc toggle and GRAB draw/holster notifies, preserving the spy BTW path. | All five connected grab/wake variants failed with loaded=0 before; all five pass afterward, including firing eligibility. |
| Gas mask activates during KO | `s_LyingOnGround` called the toggle even when the mask was off. Restored the original `SBPawn.bGasMask` condition. | Three of four equipment regression cases failed before; all four pass after. The active mask is removed; an unused mask stays off without spending air. |
| Gun stays fixed on screen during head-jump KO | The fall and wake clips both contain `NotifyDrawUndrawWeapon`, which previously only dispatched to spy BTW logic. | Connected actual-content fall/wake test observes lowered then hidden rifle, restored owner/server weapon state, visible gun and available fire. State-entry fixture validates recovery, not collision-hit detection. |
| Ordinary merc jumps use long recovery | Original landing rules deliberately select high recovery for an unarmed merc. The KO defect left the merc unarmed. | Fresh armed stationary/moving jumps pass at 100 ms; an ordinary jump after actual-content KO recovery also avoids both high-landing actions. No gravity/height thresholds changed. |
| Running persists under results | Authority retained held acceleration and skipped remote walking physics once sequenced input stopped. Clear acceleration on GameEnded and continue ordinary zero-input braking/falling. | Connected spy and merc tests verify owner/server velocity settles and locomotion animation weights reach zero. |
| Camera needs two presses / loses state | A hand-written combat RPC could execute during packet decoding before the preceding deferred controller-state RPC. Route both through the same reliable input fence and revalidate pawn lifetime on execution. A stale relevance origin also prevented the newly selected camera's ownership from reaching the viewer. Rendered testing then exposed an unchanged-angle reliable stream starving movement; coalesce angles after movement within the budget, at most 20 Hz. | First-entry, switching and constrained-bandwidth regressions failed before their respective fixes. Nine connected camera/lifetime/weapon cases pass afterward. The camera cases also check pawn, camera and spectator relevance origins, sustained angle changes and dropping pending angles from the previous camera. |
| Camera turns right without input | The decoded clamp returned world angle 0 when angle equalled the limit centre. Preserve that centre when no change is needed. | Focused clamp and connected no-input view checks. This is an intentional correction of an original script edge case. |
| Disabled camera loses its frame | Opaque black feed cover was drawn above the whole camera HUD. Draw it below the camera frame and controls. | Rendered disabled capture inspected: amber frame, readouts, camera name and controls remain around the countdown. |
| Spy camera gas invisible to merc | Relevance used the server controller's stale location instead of the current authoritative pawn/camera/spectator view. Supply both viewing actor and position from gameplay state. | Both Aquarius regression cases failed before and pass afterward; all four TestBox/Aquarius orientation cases pass. Real rendered spy-host/merc-client gas is now visible, expires and spends one dose. |
| “Can't spawn mine here” | Current original-derived English content already says “Can't place mine here”. Normalize the exact legacy English fallback at runtime, retaining other translations. | Runtime-only; no content repack required. |
| Quiet spy roll/footsteps | No demonstrated gain defect. Local footstep gain 1.0, original speed/radius formulas and roll notify volume/radius agree with retained original data. Own-pawn occlusion-distance attenuation is skipped. Doppler excludes own-source velocity but retains listener velocity and affects pitch, not volume. | No arbitrary volume change. Subjective original/game listening comparison remains unverified. |

## Rendered evidence

- **PASS** `20261101-134940-e919390a`: Aquarius, spy listen host / merc client, typical impaired UDP,
  144/30 FPS caps. Normal grab/KO/wake, then normal firing and grenade input. Assertions require loaded
  and visible weapon, gas mask off, authoritative ammo and grenade charge decreasing. The final merc
  screenshot was inspected: arms/rifle visible, 23/123 ammunition, three frag grenades.
- **PASS** `20261101-135507-1b5102da`: Aquarius camera gas, typical impaired UDP, spy host / merc client.
  Each view receives one gas actor and draws its particles (guest peak 15); expiry and exactly one dose
  are checked. The guest screenshot was inspected and clearly shows the cloud. Camera placement is an
  explicit collision-checked fixture; gas release uses normal input.
- **PASS** `20261101-135233-aa758e06`: rendered moving-results check over real UDP. Merc held movement
  when final-life damage ended the round. Authority/owner velocity became zero and all locomotion
  blends faded; inspected captures show the correct You Win / You Lose results. Damage was an explicit fixture.
- **PASS** `20261101-140814-789748aa`: Aquarius camera network, LAN UDP proxy, spy host / merc client,
  60/60 FPS. First right click enters Entrance; 74 samples over eight idle seconds retain yaw 49100.
  Six seconds of normal mouse input turn the view without starving movement. Switching selects Tropical
  Fish Room with correct ownership. A damage fixture disables it; the inspected screenshot retains the
  full frame/controls around the countdown. One right click exits to walking with the rifle visible.
- **PASS** `20261101-141258-85d905ee`: clean Windows release export from pushed commit `a286408`,
  repeating the Aquarius grab/KO/wake/fire/grenade scenario over impaired UDP at 144/30 FPS. Inspected
  the final merc capture: visible arms/rifle, ammunition 23/123 and three grenades. The lab records the
  actual exported assembly/executable hashes; both game processes and proxy stopped normally.

These are independent rendered processes on this PC, not a physical PC/laptop LAN acceptance run.

## Additional bandwidth regression

The full suite caught the Steel Squat story fixture slightly exceeding its unchanged 12,288 B/s limit.
Tracing found a merc's hidden first-person rifle reference repeatedly dirty on observer channels. Single-rifle
observers already render `WeaponDummy`; they now receive `PrimaryWeapon` only when a sidearm makes both
carried weapons relevant. The owner always receives it. The new connected regression verifies idle traffic,
later sidearm grant and removal; existing sidearm late-join, fire and reload cases also pass.
The exact default SteeE C/spies/63 match now passes: Spy1 traffic falls from 12,305 to **11,547 B/s**,
and the two mercs' property bunches to that observer fall from 23,448 to **63**. All clients agree on
the result with zero reinitializations. No bandwidth limit or gameplay threshold was relaxed.

## Release checks

- Both solutions build with zero warnings/errors, including a fresh detached checkout of the pushed
  release commit. The pinned Godot Windows export succeeded.
- Game-data guard, vendored converter patch check and banned-API enforcement passed.
- The full local suite completed in 15m48s: **3,059 passed, 140 skipped, one failed**. All integration,
  networking, client-logic and physics test projects pass. The one strict allocation-test failure is:
  `MateSearchAllocationTests.TheMateEntriesAllocateNothingAfterWarmUp` measured 7,480 bytes against zero.
  One focused rerun of the unchanged executable passed with zero allocations. Its local spy interaction
  path does not exercise these fixes; the cause is unproven. This is not represented as a clean full-suite pass.
- Initial CI found that the new authored-animation test did not skip on machines lacking converted
  characters. Test-only commit `5d51635` adds the existing character-content prerequisite and leaves
  its real-animation assertions intact: five cases pass with installed content; four pass and the one
  dependent case skips in an isolated empty-content checkout. Production code did not change.
- Final Windows, Linux and server builds use **`5d516353e2c97d1628196717b22ec12b8c4324af`**. The final
  Windows export passes boot and TestBox (four walkers, maximum error 0.03 uu). An initial raw-export
  TestBox attempt lacked the release fixture; the correctly staged release-layout check passes.
- Final CI: [run 41315825009](https://github.com/Lumbridge/OpenSCCT/actions/runs/41315825009).
  **Linux, Windows and Godot jobs all passed** on the final commit. Both .NET platforms report
  2,709 passed, 491 skipped and zero failures. The earlier failed run is retained as evidence of the
  corrected missing-content prerequisite, not described as a successful run.
- Linux workflow `41315849017` passed native boot/TestBox and installed Flatpak sandbox checks. Its
  artifact metadata records the final commit, `linux-x64` and `dirty=false`.
  The Flatpak SHA-256 is `1b2a7e6abe9bfa28165ec283948c519f258be79dd36b7a0fe45458c90ac9380d`.
- Windows release verification downloaded all 12 newly uploaded game-file assets and matched their
  SHA-256 hashes. All 207 new decompressed payloads match their content hashes; 218 exported files
  match the manifest; all 125 referenced storage blobs exist at the advertised sizes. The manifest
  contains 2,003 files and points at the release commit. No display override or converted content ships.
- The final release's launcher and Flatpak were downloaded again and matched their local SHA-256
  hashes. Launcher 0.8.25 is unchanged. Existing published content packs are reused.

## Still outstanding

- Subjective spy footstep/roll listening comparison against the original. The gain audit did not
  justify changing volume, so this report does not mark the reported quietness as fixed.
- Physical PC/laptop testing and the broader floor/pose, prolonged input-recovery, death-animation
  and interaction coverage from the [overnight report](NETWORK-OVERNIGHT-REPORT-2026-11-01.md#outstanding-work-in-priority-order).
  These focused fixes do not certify those remaining cases or perfect networking.

## Deployment

- `svm-master`, `svm-server` and `svm-hub` were deployed with `Deploy-Vps.ps1` from the clean release
  checkout after confirming both listed services were empty. All three systemd services are active.
- Public status reports game **0.8.54**, master commit **5d51635** and latest version **0.8.54**.
  The minimum remains 0.8.53 because the wire format is unchanged. Both players should update to
  receive the client and server fixes together.
- The dedicated server and EU Hub register as trusted, direct-reachability **0.8.54** services.
  Deployed Hub manifest and layout/config SHA-256 values match the checkout's fixtures.
- GitHub marks **v0.8.54** as the published latest release, with the final commit as its tag target.
  The Windows launcher, Linux Flatpak and checksum file are live; the game-update manifest and all
  referenced payloads are available and verified as described above.
- Configuration and service backups were retained under `/opt/svm/backups`. Existing accounts,
  service configuration other than the latest-version notice, and downloaded game content are preserved.

The source tree initially had unrelated `reports/` and `research_notes/` directories; concurrent
`tools/deploy/us-lobbies/` work is also outside this change. These are excluded from the release commit.
