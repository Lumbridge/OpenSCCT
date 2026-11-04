# Heartbeat sensor (SGadHeartBeatSensor)

Units:
- def:SBase.SGadHeartBeatSensor, uc:SBase.SGadHeartBeatSensor:Load, uc:SBase.SGadHeartBeatSensor:Tick,
  nat:SBase.SGadHeartBeatSensor.CheckForPawn

## Purpose
The spy's heartbeat sensor marks every pawn within 2200 uu in a 60 degree cone ahead of the spy's view while the
sensor is the selected gadget and the Shocker is drawn. Marked pawns show on the spy's radar.

## Data
| Field | Default | Notes |
|---|---|---|
| Charge | -10 | never spent (Use is the base, empty) |
| RefireRate | 0.2 s | not read by the sensor |
| fDetectionDistance | 2200 uu | config |
| fDemiAngle | 0.866 | cosine of the half angle (30 degrees), config |
| StateOfDetect | 0 | 0 not scanning, 1 something detected, 2 nothing detected |
| bActivated, bReloading, fDateActivation | false, false, 0 | no script or native reader in 4.0 |
| Sounds | activation Hud_bipInfo2, check echo_indic1, detected Heart_Beat | for the HUD |
| SBasePawn.bDetectedByHBS | false | set and cleared by the scan, read by the radar |
| ItemName / MagmaIconName | "Heart Sensor" / A_HeartSensor | |

## Rules
1. **Tick** (simulated, every machine that has the gadget: the server and the owner's client): `CheckForPawn()`.
2. **CheckForPawn** (native 1821, decoded 0x10abd0e0), returns true when it scanned:
   1. needs the owner pawn, the owner's `SelectedGadget` being this sensor, and the owner's weapon drawn
      (`WeaponLoaded != 0`); otherwise nothing changes (marks from an earlier scan stay). Draw and holster states
      3 and 4 still scan, and `bWeaponTransit` does not gate detection.
   2. `StateOfDetect = 0`. The view direction V is the owner's controller rotation as a vector.
   3. For each pawn of the level's pawn list (in order), other than the owner: when the squared distance from the
      owner is under `fDetectionDistance^2` and `V . normal(pawn - owner) > fDemiAngle`, mark it
      (`bDetectedByHBS = true`) and set `StateOfDetect = 1`; otherwise clear its mark. Distances are 3D; there is no
      team, health or line-of-sight test (team mates, dead pawns and pawns behind walls are marked too).
   4. When nothing was found, `StateOfDetect = 2`.
3. **Load** is empty (no message on selection).
4. **Radar and sounds** (HUD): the spy radar runs before the heart cone page. Other spies use teammate icons;
   mercs marked by the sensor use enemy icons only when the cone page set its detected latch on the previous HUD
   frame. The native radar consumes each displayed merc's mark; the next simulation tick refreshes it. The port
   retains marks between rendered frames because simulation runs at 60 Hz, and uses the same previous-frame latch
   to stop showing them after holstering or deselection. Spy-bullet tags continue to work independently.
   The HUD's heart cone page
   (game 0x10b2d7a0, spec/equipment/gadget-hud.md rules 22 to 28) plays the sounds: `Snd_DetectedPawn` (Heart_Beat, a
   2D wave of 1.6 s) while `StateOfDetect == 1`, at most once every 1.5 s, and `Snd_Check` (echo_indic1) each time the
   cone loop wraps. The heartbeat is one fixed sound, not a pawn's: its rate does not follow distance, health or how
   many pawns were found. Both go through the HUD sound helper (0x10b26920): no actor, slot 0, volume 1, radius 4096,
   pitch 1, the 2D flag. Only the spy using the sensor hears them, on his own machine.
5. With the weapon holstered the page calls StopSound(PlayerOwner, Snd_DetectedPawn). The heartbeat was started with
   no actor, so that stop does not match it. A controlled original capture confirms the active beat continues to its
   natural end after `WeaponLoaded` reaches 0. The port likewise leaves the active wave playing.

## States
None.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Range | 2200 | uu | decoded | def (config) |
| Half angle cosine | 0.866 | | decoded | def (config) |
| Comparisons | distance strict below, cosine strict above | | decoded | 0x10abd193, 0x10abd211 |

## Network
- Client and server each scan with their own view of the pawns; nothing is sent. The flag is local to each machine,
  and only the spy's own HUD reads it. The sounds come from that HUD, so they are local too, online as offline.

## Edge cases
- Fully holstering, or selecting another gadget, stops the scan without clearing its state. This does not leave
  permanent radar blips: the HUD clears its detected latch, and the following HUD frame stops drawing sensor marks.
- Holstering does not stop it at the key press: WeaponLoaded stays non-zero until the holster clip's
  NotifyUndrawWeapon (about 0.15 s in) sets 4 and the pistol's backward draw clip ends (4 frames at 15 fps), so the
  scan and the cone run on for about 0.4 s (spy-shocker.md rules 4 and 5).
- The cone uses the controller rotation (the camera), not the pawn's facing.

## Evidence
- local/re-out/uc/SBase/SGadHeartBeatSensor.uc; game native 0x10abd0e0 (private Ghidra copy local/ghidra-work/gadgets);
  local/reflect/game.json: SBasePawn.bDetectedByHBS bit 0x2 at 0xaf4, SGadHeartBeatSensor fields 0x35c to 0x368.
- HUD heart cone page 0x10b2d7a0 and its sound helper 0x10b26920 (Audio vtable 0x70 PlaySound; the holster stop is
  vtable 0x74, the slot execStopSound 0x10a25720 calls), private copy local/ghidra-work/heart.
- Radar 0x10b2d410: prior cone latch read at 0x10b2d5a9, detection bit test at 0x10b2d5b3 and clear at
  0x10b2d5c4. Local disassembly: local/re-out/heartbeat-parity/10b2d410.asm.txt. Update order and projection are
  also recorded in SCCT_Versus_UE5_Bridge/docs/overlays/radar.md, sections 3.1, 3.2 and 3.5.
- 2026-11-04 controlled original scanner captures on SteeECombat: local/heartbeat-av-parity/original/capture.json
  and original-baseline/capture.json, with presented screenshots, native area clocks and sound-source transitions.
  The heartbeat starting at 36.4505 s continued until 38.0826 s although holstering completed by 36.9322 s.
- Audio comparison: local/heartbeat-av-parity/source-wave-comparison.json and
  rewrite-final/rendered-audio-comparison.json. Both source WAVs match original package bytes; actual port output
  versus those samples at authored gain has correlation above 0.9997 and gain error below 0.005 dB. Original sound
  routing/timing was observed while muted; this is not a DirectSound loopback loudness comparison.
- Port driver capture: local/heartbeat-av-parity/rewrite-final/rewrite-driver-timeline.json. At 60 Hz the strict
  heartbeat interval is 1.5167 s, with scan pings 2.0 s apart and detected pings 1.8 s apart. Overlapping heartbeats
  and the unmatched controller stop were also checked against actual captured port mixer output.
- Stock visual reference: local/heartbeat-av-parity/original-stock-hud, with Reloaded's widescreen HUD patch
  disabled and 640x480 output. Both authored arcs are visible. The patched reference suppresses `onde2` through a
  shared widget-name positioning rule; it must not be used to infer the stock scanner's wave styling.
- Matching production GPU renders cover six phases in both scanning and detection. The fixed cone rectangle's
  average absolute channel difference falls from 8.74 to 1.06 out of 255; source geometry and colors are retained
  alongside the screenshots. See local/heartbeat-av-parity/stock-visual-comparison.json and stock-scanner-comparison.png.

## Comparison limits
- The controlled captures cover stationary scanning, detection, selection and holstering. They do not establish
  every map's visual appearance or hardware-dependent output latency. Image differences include animated scene
  background and rasterizer sampling; the stock reference's evaluated transforms bound the animation phase.
  The enemy blip has its own unsynchronized pulse clock, so its size differs between some paired captures.
