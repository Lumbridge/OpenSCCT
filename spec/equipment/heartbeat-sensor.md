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
      (`WeaponLoaded != 0`); otherwise nothing changes (marks from an earlier scan stay).
   2. `StateOfDetect = 0`. The view direction V is the owner's controller rotation as a vector.
   3. For each pawn of the level's pawn list (in order), other than the owner: when the squared distance from the
      owner is under `fDetectionDistance^2` and `V . normal(pawn - owner) > fDemiAngle`, mark it
      (`bDetectedByHBS = true`) and set `StateOfDetect = 1`; otherwise clear its mark. Distances are 3D; there is no
      team, health or line-of-sight test (team mates, dead pawns and pawns behind walls are marked too).
   4. When nothing was found, `StateOfDetect = 2`.
3. **Load** is empty (no message on selection).
4. **Radar** (HUD, estimated): the spy's radar draws marked pawns while the sensor scans; the check sound plays with
   the sweep and the heartbeat sound while something is detected. The drawing belongs to the HUD spec.

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
  and only the spy's own HUD reads it.

## Edge cases
- Holstering, or selecting another gadget, stops the scan without clearing the marks: the radar keeps showing the
  last result until the next scan.
- The cone uses the controller rotation (the camera), not the pawn's facing.

## Evidence
- local/re-out/uc/SBase/SGadHeartBeatSensor.uc; game native 0x10abd0e0 (private Ghidra copy local/ghidra-work/gadgets);
  local/reflect/game.json: SBasePawn.bDetectedByHBS bit 0x2 at 0xaf4, SGadHeartBeatSensor fields 0x35c to 0x368.

## Open questions
- The radar drawing of `bDetectedByHBS` (SHud.HudAtt native) and the sound cadence.
